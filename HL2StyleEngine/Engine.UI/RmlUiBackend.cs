using Engine.Render;
using Engine.UI.Native;
using Engine.UI.Rendering;
using Engine.UI.Rml;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Net;

namespace Engine.UI;

public sealed class RmlUiBackend : IDisposable
{
    public const string DefaultNativeBridgeName = "HS2RmlUiBridge";
    private const string TestDocumentEnvironmentVariable = "HS2_RMLUI_TEST_DOCUMENT";
    private const string RuntimeDocument = "Runtime/gameplay_ui.rml";

    private readonly IntPtr _nativeBridgeHandle;
    private readonly RmlUiNativeApi? _nativeApi;
    private readonly RmlUiOverlayRenderer _overlayRenderer = new();
    private readonly RmlUiTextImageCache _textImageCache;
    private GameplayUiState _state = new();
    private string _lastDocumentHash = "";
    private string _pendingDocument = "";
    private bool _documentDirty = true;
    private IntPtr _context;
    private IntPtr _gameplayDocument;
    private int _hoveredDataSlot = -1;
    private bool _overlayPrepared;
    private IntPtr _resourceDocument;
    private readonly Queue<string> _pendingIcons = new();
    private readonly HashSet<string> _queuedIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _preparedNames = new(StringComparer.Ordinal);
    private long _queuedIconBytes;
    private long _preparedTitleBytes;
    private const long IconPreloadBudget = 16 * 1024 * 1024;
    public long PreloadedIconBytes => _queuedIconBytes;

    private RmlUiBackend(string contentRoot, IntPtr nativeBridgeHandle, RmlUiNativeApi? nativeApi, string status)
    {
        ContentRoot = contentRoot;
        _nativeBridgeHandle = nativeBridgeHandle;
        _nativeApi = nativeApi;
        _textImageCache = new RmlUiTextImageCache(contentRoot);
        Status = status;
    }

    public string Name => "RmlUi";
    public UiWorkProfile? Profile { get => _textImageCache.Profile; set => _textImageCache.Profile = value; }
    public string ContentRoot { get; }
    public string Status { get; private set; }
    public string RenderStatus => _overlayRenderer.Status;
    public bool HasSubmittedOverlayFrame => _overlayRenderer.LastRenderSubmitted;
    public int LastSubmittedCommands => _overlayRenderer.LastSubmittedCommands;
    public string OverlayDebugStatus => _overlayRenderer.Status;
    public bool TestDocumentEnabled { get; } = TestDocumentOptedIn();

    public bool NativeBridgeFound => _nativeBridgeHandle != IntPtr.Zero;
    public bool HasNativeApi => _nativeApi != null;

    public bool IsReady => _nativeApi != null && _context != IntPtr.Zero;

    public bool TryGetHoveredDataSlot(out int slot)
    {
        slot = _hoveredDataSlot;
        return slot >= 0;
    }

    public static RmlUiBackend Probe(string contentRoot, string nativeBridgeName = DefaultNativeBridgeName)
    {
        string normalizedRoot = Path.GetFullPath(contentRoot);

        if (NativeLibrary.TryLoad(nativeBridgeName, out IntPtr bridgeHandle))
        {
            if (!RmlUiNativeApi.TryBind(bridgeHandle, out RmlUiNativeApi api, out string bindError))
            {
                return new RmlUiBackend(
                    normalizedRoot,
                    bridgeHandle,
                    null,
                    $"RmlUi native bridge found, but it is not compatible yet. {bindError}");
            }

            return new RmlUiBackend(
                normalizedRoot,
                bridgeHandle,
                api,
                "RmlUi native bridge found. Waiting for first frame to create context.");
        }

        return new RmlUiBackend(
            normalizedRoot,
            IntPtr.Zero,
            null,
            $"RmlUi native bridge '{nativeBridgeName}' not found. ImGui gameplay UI fallback is available.");
    }

    public void SubmitState(GameplayUiState state)
    {
        _state = state;
        string document;
        using (Profile?.Measure("document-build"))
            document = TestDocumentEnabled ? BuildTestDocument(state) : RmlUiDocumentBuilder.Build(state, _textImageCache.TryGetTextImage);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document)));
        if (hash == _lastDocumentHash)
            return;

        _lastDocumentHash = hash;
        _pendingDocument = document;
        _documentDirty = true;
        using (Profile?.Measure("document-write")) WriteRuntimeDocument(document);

        if (_nativeApi != null && _gameplayDocument != IntPtr.Zero)
        {
            using var refresh = Profile?.Measure("native-refresh");
            if (_nativeApi.TrySetDocumentBody(_gameplayDocument, document))
                _documentDirty = false;
            else
                Status = "RmlUi state updated on disk; native bridge failed live UI refresh.";
        }
    }

    public void Update(RmlUiFrameContext context)
    {
        using var measure = Profile?.Measure("native-update");
        if (_nativeApi == null)
            return;

        EnsureContext(context);

        if (!IsReady)
            return;

        _nativeApi.SetViewport(_context, context.ViewportWidth, context.ViewportHeight);
        _nativeApi.SetMousePosition(_context, context.Input.MousePosition.X, context.Input.MousePosition.Y);
        _nativeApi.SetMouseButton(_context, 0, context.Input.LeftMouseDown);
        _nativeApi.SetMouseButton(_context, 1, context.Input.RightMouseDown);
        _nativeApi.Update(_context, context.DeltaTime);
        _hoveredDataSlot = _nativeApi.TryGetHoveredDataSlot(_context, out int hoveredSlot)
            ? hoveredSlot
            : -1;
    }

    public void RenderOverlay(Renderer renderer)
    {
        using var measure = Profile?.Measure("native-render-upload");
        if (!IsReady)
            return;

        using (Profile?.Measure("native-geometry")) _nativeApi!.Render(_context);
        using (Profile?.Measure("native-texture-upload")) UploadDirtyTextures(renderer);

        if (!_nativeApi.TryGetRenderData(_context, out RmlUiRenderData renderData))
        {
            Status = "RmlUi bridge rendered, but did not provide render data.";
            return;
        }

        try
        {
            using (Profile?.Measure("native-submit")) _overlayRenderer.Render(renderer, renderData);
        }
        finally
        {
            _nativeApi.ReleaseRenderData(_context);
        }
    }

    public void Dispose()
    {
        if (_nativeApi != null && _context != IntPtr.Zero)
        {
            _nativeApi.DestroyContext(_context);
            _context = IntPtr.Zero;
            _gameplayDocument = IntPtr.Zero;
        }

        if (_nativeBridgeHandle != IntPtr.Zero)
            NativeLibrary.Free(_nativeBridgeHandle);

        _textImageCache.Dispose();
        _overlayRenderer.Dispose();
    }

    public void PrewarmOverlay(Renderer renderer)
    {
        if (!IsReady) return;
        using var measure = Profile?.Measure("native-prewarm");
        PrewarmItemResources(renderer);
        if (_overlayPrepared) return;
        // Prepare the currently loaded document's resources without drawing it or changing focus.
        _nativeApi!.Render(_context);
        UploadDirtyTextures(renderer);
        _overlayPrepared = _overlayRenderer.Prepare(renderer);
        if (_nativeApi.TryGetRenderData(_context, out var data))
        {
            try { _overlayRenderer.Render(renderer, data, submit: false); }
            finally { _nativeApi.ReleaseRenderData(_context); }
        }
    }

    public void QueuePreloadIcon(string path, long bytes)
    {
        try { path = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (bytes <= 0 || bytes > IconPreloadBudget - _queuedIconBytes || _queuedIcons.Count >= 64 ||
            !path.StartsWith(Path.TrimEndingDirectorySeparator(ContentRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !_queuedIcons.Add(path)) return;
        _queuedIconBytes += bytes;
        _pendingIcons.Enqueue(path);
    }

    private void PrewarmItemResources(Renderer renderer)
    {
        string images = "";
        if (_pendingIcons.TryDequeue(out string? icon))
            images = ImageTag(Path.GetRelativePath(Path.Combine(ContentRoot, "Runtime"), icon).Replace('\\', '/'));
        // The selected-item title is the only raster text in the main inventory grid.
        // Warm current/save item names incrementally, not arbitrary dynamic strings.
        if (_preparedNames.Count < 64)
        {
            var item = _state.InventoryItems.Concat(_state.StorageItems).FirstOrDefault(i => i.DisplayName.Length <= 256 && !_preparedNames.Contains(i.DisplayName));
            if (item != null)
            {
                _preparedNames.Add(item.DisplayName);
                if (_textImageCache.TryGetTextImage(item.DisplayName, "subtitle", 16, out var text))
                {
                    long bytes = (long)text.Width * text.Height * 4;
                    if (bytes <= 4 * 1024 * 1024 - _preparedTitleBytes)
                    {
                        images += ImageTag(text.Source);
                        _preparedTitleBytes += bytes;
                    }
                }
            }
        }
        if (images.Length == 0) return;
        const string resourcePath = "Runtime/preload_ui_resources.rml";
        if (_resourceDocument == IntPtr.Zero)
        {
            Directory.CreateDirectory(Path.Combine(ContentRoot, "Runtime"));
            File.WriteAllText(Path.Combine(ContentRoot, resourcePath), "<rml><head></head><body></body></rml>");
            if (!_nativeApi!.LoadDocument(_context, resourcePath, out _resourceDocument)) return;
        }
        // Never shown or focused. Loading each resource here populates RmlUi's own
        // texture cache; ImGui's image cache cannot satisfy native texture requests.
        if (_nativeApi!.TrySetDocumentBody(_resourceDocument, "<rml><head></head><body>" + images + "</body></rml>"))
            UploadDirtyTextures(renderer);
        static string ImageTag(string path) => "<img src='" + WebUtility.HtmlEncode(path) + "' width='1' height='1'/>";
    }

    private void EnsureContext(RmlUiFrameContext frameContext)
    {
        if (_nativeApi == null || _context != IntPtr.Zero)
            return;

        if (!_nativeApi.CreateContext(ContentRoot, frameContext.ViewportWidth, frameContext.ViewportHeight, out _context))
        {
            Status = "RmlUi native bridge loaded, but context creation failed.";
            return;
        }

        if (_documentDirty && !string.IsNullOrWhiteSpace(_pendingDocument))
            WriteRuntimeDocument(_pendingDocument);

        if (!_nativeApi.LoadDocument(_context, RuntimeDocument, out _gameplayDocument))
        {
            Status = $"RmlUi context created, but failed to load '{RuntimeDocument}'.";
            return;
        }

        _nativeApi.ShowDocument(_gameplayDocument);
        _documentDirty = false;
        Status = TestDocumentEnabled
            ? $"RmlUi context ready with diagnostic '{RuntimeDocument}'."
            : $"RmlUi context ready with generated '{RuntimeDocument}'.";
    }

    private void UploadDirtyTextures(Renderer renderer)
    {
        if (_nativeApi == null || _context == IntPtr.Zero)
            return;

        if (!_nativeApi.TryGetTextureData(_context, out IntPtr textures, out int count) ||
            textures == IntPtr.Zero ||
            count <= 0)
        {
            return;
        }

        try
        {
            int textureSize = Marshal.SizeOf<RmlUiTextureData>();
            for (int i = 0; i < count; i++)
            {
                IntPtr texturePtr = IntPtr.Add(textures, i * textureSize);
                RmlUiTextureData texture = Marshal.PtrToStructure<RmlUiTextureData>(texturePtr);
                if (texture.TextureId == 0 ||
                    texture.Rgba == IntPtr.Zero ||
                    texture.Width <= 0 ||
                    texture.Height <= 0 ||
                    texture.ByteCount <= 0)
                {
                    continue;
                }

                byte[] rgba = new byte[texture.ByteCount];
                Marshal.Copy(texture.Rgba, rgba, 0, rgba.Length);
                _overlayRenderer.RegisterTextureRgba(texture.TextureId, renderer, texture.Width, texture.Height, rgba);
            }
        }
        finally
        {
            _nativeApi.ReleaseTextureData(_context);
        }
    }

    private void WriteRuntimeDocument(string document)
    {
        string runtimeDirectory = Path.Combine(ContentRoot, "Runtime");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllText(Path.Combine(ContentRoot, RuntimeDocument), document, Encoding.UTF8);
    }

    private static bool TestDocumentOptedIn()
    {
        string? value = Environment.GetEnvironmentVariable(TestDocumentEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTestDocument(GameplayUiState state)
    {
        string message = string.IsNullOrWhiteSpace(state.GameMessage)
            ? "Native RmlUi diagnostic panel"
            : state.GameMessage;

        return $$"""
<rml>
  <head>
    <title>RmlUi Diagnostic</title>
    <style>
      body {
        font-family: serif;
        color: #dedbd0;
      }

      #diagnostic-panel {
        position: absolute;
        left: 48px;
        top: 48px;
        width: 560px;
        height: 260px;
        padding: 28px;
        background-color: rgba(7, 16, 18, 0.95);
        border: 3px #d8d6c4;
      }

      h1 {
        margin: 0 0 12px 0;
        font-size: 34px;
        color: #f4efd9;
      }

      p {
        margin: 8px 0;
        font-size: 18px;
        color: #c9d3c8;
      }

      #diagnostic-counts {
        font-size: 16px;
        color: #8da89a;
      }

      #diagnostic-icon {
        position: absolute;
        left: 28px;
        bottom: 26px;
        width: 96px;
        height: 96px;
        background-color: #9f7842;
        border: 2px #f2e8ca;
      }

      #diagnostic-icon img {
        width: 96px;
        height: 96px;
      }

      #diagnostic-status {
        position: absolute;
        left: 146px;
        bottom: 26px;
        width: 320px;
        height: 96px;
        padding: 16px;
        background-color: #273b38;
        border: 1px #77ccb2;
      }

      #diagnostic-status p {
        margin: 0;
        color: #e7eadc;
        font-size: 18px;
      }
    </style>
  </head>
  <body>
    <div id="diagnostic-panel">
      <h1>RmlUi Native Test</h1>
      <p>{{EscapeRml(message)}}</p>
      <p id="diagnostic-counts">Inventory: {{(state.InventoryOpen ? "open" : "closed")}} | Items: {{state.InventoryItems.Count}} | Saves: {{state.SaveCount}}</p>
      <div id="diagnostic-icon">
        <img src="../Icons/InkRibbon.png" width="96" height="96" />
      </div>
      <div id="diagnostic-status">
        <p>If you see this card and the icon, native RmlUi is drawing styled UI.</p>
      </div>
    </div>
  </body>
</rml>
""";
    }

    private static string EscapeRml(string text)
        => text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
}
