using Engine.Render;
using Engine.UI.Native;
using Engine.UI.Rendering;
using Engine.UI.Rml;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Engine.UI;

public sealed class RmlUiBackend : IDisposable
{
    public const string DefaultNativeBridgeName = "HS2RmlUiBridge";
    private const string RuntimeDocument = "Runtime/gameplay_ui.rml";

    private readonly IntPtr _nativeBridgeHandle;
    private readonly RmlUiNativeApi? _nativeApi;
    private readonly RmlUiOverlayRenderer _overlayRenderer = new();
    private GameplayUiState _state = new();
    private string _lastDocumentHash = "";
    private string _pendingDocument = "";
    private bool _documentDirty = true;
    private IntPtr _context;
    private IntPtr _gameplayDocument;

    private RmlUiBackend(string contentRoot, IntPtr nativeBridgeHandle, RmlUiNativeApi? nativeApi, string status)
    {
        ContentRoot = contentRoot;
        _nativeBridgeHandle = nativeBridgeHandle;
        _nativeApi = nativeApi;
        Status = status;
    }

    public string Name => "RmlUi";
    public string ContentRoot { get; }
    public string Status { get; private set; }
    public string RenderStatus => _overlayRenderer.Status;

    public bool NativeBridgeFound => _nativeBridgeHandle != IntPtr.Zero;

    public bool IsReady => _nativeApi != null && _context != IntPtr.Zero;

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
            $"RmlUi native bridge '{nativeBridgeName}' not found. ImGui gameplay UI fallback is active while RmlUi assets are generated.");
    }

    public void SubmitState(GameplayUiState state)
    {
        _state = state;
        string document = RmlUiDocumentBuilder.Build(state);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document)));
        if (hash == _lastDocumentHash)
            return;

        _lastDocumentHash = hash;
        _pendingDocument = document;
        _documentDirty = true;
        WriteRuntimeDocument(document);

        if (_nativeApi != null && _gameplayDocument != IntPtr.Zero)
        {
            if (_nativeApi.TrySetDocumentBody(_gameplayDocument, document))
                _documentDirty = false;
            else
                Status = "RmlUi state updated on disk; native bridge needs hs2_rmlui_set_document_body for live UI refresh.";
        }
    }

    public void Update(RmlUiFrameContext context)
    {
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
    }

    public void RenderOverlay(Renderer renderer)
    {
        if (!IsReady)
            return;

        _nativeApi!.Render(_context);
        if (!_nativeApi.TryGetRenderData(_context, out RmlUiRenderData renderData))
        {
            Status = "RmlUi bridge rendered, but did not provide render data.";
            return;
        }

        try
        {
            _overlayRenderer.Render(renderer, renderData);
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

        _overlayRenderer.Dispose();
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
        Status = $"RmlUi context ready with generated '{RuntimeDocument}'. ImGui gameplay UI fallback can be disabled.";
    }

    private void WriteRuntimeDocument(string document)
    {
        string runtimeDirectory = Path.Combine(ContentRoot, "Runtime");
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllText(Path.Combine(ContentRoot, RuntimeDocument), document, Encoding.UTF8);
    }
}
