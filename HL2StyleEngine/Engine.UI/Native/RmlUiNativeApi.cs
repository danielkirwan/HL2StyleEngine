using System.Runtime.InteropServices;

namespace Engine.UI.Native;

internal sealed class RmlUiNativeApi
{
    private const CallingConvention Convention = CallingConvention.Cdecl;

    private readonly CreateContextFn _createContext;
    private readonly DestroyContextFn _destroyContext;
    private readonly SetViewportFn _setViewport;
    private readonly LoadDocumentFn _loadDocument;
    private readonly ShowDocumentFn _showDocument;
    private readonly HideDocumentFn _hideDocument;
    private readonly UpdateFn _update;
    private readonly RenderFn _render;
    private readonly SetMousePositionFn _setMousePosition;
    private readonly SetMouseButtonFn _setMouseButton;
    private readonly SetKeyFn _setKey;
    private readonly SubmitTextFn _submitText;
    private readonly GetRenderDataFn _getRenderData;
    private readonly ReleaseRenderDataFn _releaseRenderData;
    private readonly SetDocumentBodyFn? _setDocumentBody;

    private RmlUiNativeApi(
        CreateContextFn createContext,
        DestroyContextFn destroyContext,
        SetViewportFn setViewport,
        LoadDocumentFn loadDocument,
        ShowDocumentFn showDocument,
        HideDocumentFn hideDocument,
        UpdateFn update,
        RenderFn render,
        SetMousePositionFn setMousePosition,
        SetMouseButtonFn setMouseButton,
        SetKeyFn setKey,
        SubmitTextFn submitText,
        GetRenderDataFn getRenderData,
        ReleaseRenderDataFn releaseRenderData,
        SetDocumentBodyFn? setDocumentBody)
    {
        _createContext = createContext;
        _destroyContext = destroyContext;
        _setViewport = setViewport;
        _loadDocument = loadDocument;
        _showDocument = showDocument;
        _hideDocument = hideDocument;
        _update = update;
        _render = render;
        _setMousePosition = setMousePosition;
        _setMouseButton = setMouseButton;
        _setKey = setKey;
        _submitText = submitText;
        _getRenderData = getRenderData;
        _releaseRenderData = releaseRenderData;
        _setDocumentBody = setDocumentBody;
    }

    public static bool TryBind(IntPtr library, out RmlUiNativeApi api, out string error)
    {
        api = null!;

        if (!TryExport(library, "hs2_rmlui_create_context", out CreateContextFn createContext, out error) ||
            !TryExport(library, "hs2_rmlui_destroy_context", out DestroyContextFn destroyContext, out error) ||
            !TryExport(library, "hs2_rmlui_set_viewport", out SetViewportFn setViewport, out error) ||
            !TryExport(library, "hs2_rmlui_load_document", out LoadDocumentFn loadDocument, out error) ||
            !TryExport(library, "hs2_rmlui_show_document", out ShowDocumentFn showDocument, out error) ||
            !TryExport(library, "hs2_rmlui_hide_document", out HideDocumentFn hideDocument, out error) ||
            !TryExport(library, "hs2_rmlui_update", out UpdateFn update, out error) ||
            !TryExport(library, "hs2_rmlui_render", out RenderFn render, out error) ||
            !TryExport(library, "hs2_rmlui_set_mouse_position", out SetMousePositionFn setMousePosition, out error) ||
            !TryExport(library, "hs2_rmlui_set_mouse_button", out SetMouseButtonFn setMouseButton, out error) ||
            !TryExport(library, "hs2_rmlui_set_key", out SetKeyFn setKey, out error) ||
            !TryExport(library, "hs2_rmlui_submit_text", out SubmitTextFn submitText, out error) ||
            !TryExport(library, "hs2_rmlui_get_render_data", out GetRenderDataFn getRenderData, out error) ||
            !TryExport(library, "hs2_rmlui_release_render_data", out ReleaseRenderDataFn releaseRenderData, out error))
        {
            return false;
        }

        SetDocumentBodyFn? setDocumentBody = null;
        if (TryExport(library, "hs2_rmlui_set_document_body", out SetDocumentBodyFn setDocumentBodyExport, out _))
            setDocumentBody = setDocumentBodyExport;

        api = new RmlUiNativeApi(
            createContext,
            destroyContext,
            setViewport,
            loadDocument,
            showDocument,
            hideDocument,
            update,
            render,
            setMousePosition,
            setMouseButton,
            setKey,
            submitText,
            getRenderData,
            releaseRenderData,
            setDocumentBody);

        error = "";
        return true;
    }

    public bool CreateContext(string contentRoot, int width, int height, out IntPtr context)
    {
        IntPtr contentRootUtf8 = Marshal.StringToCoTaskMemUTF8(contentRoot);
        try
        {
            return _createContext(contentRootUtf8, width, height, out context) != 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(contentRootUtf8);
        }
    }

    public void DestroyContext(IntPtr context)
        => _destroyContext(context);

    public void SetViewport(IntPtr context, int width, int height)
        => _setViewport(context, width, height);

    public bool LoadDocument(IntPtr context, string documentPath, out IntPtr document)
    {
        IntPtr documentPathUtf8 = Marshal.StringToCoTaskMemUTF8(documentPath);
        try
        {
            return _loadDocument(context, documentPathUtf8, out document) != 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(documentPathUtf8);
        }
    }

    public void ShowDocument(IntPtr document)
        => _showDocument(document);

    public void HideDocument(IntPtr document)
        => _hideDocument(document);

    public void Update(IntPtr context, float deltaTime)
        => _update(context, deltaTime);

    public void Render(IntPtr context)
        => _render(context);

    public void SetMousePosition(IntPtr context, float x, float y)
        => _setMousePosition(context, x, y);

    public void SetMouseButton(IntPtr context, int button, bool down)
        => _setMouseButton(context, button, down ? 1 : 0);

    public void SetKey(IntPtr context, int key, bool down)
        => _setKey(context, key, down ? 1 : 0);

    public void SubmitText(IntPtr context, string text)
    {
        IntPtr textUtf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            _submitText(context, textUtf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(textUtf8);
        }
    }

    public bool TryGetRenderData(IntPtr context, out RmlUiRenderData renderData)
        => _getRenderData(context, out renderData) != 0;

    public void ReleaseRenderData(IntPtr context)
        => _releaseRenderData(context);

    public bool TrySetDocumentBody(IntPtr document, string bodyRml)
    {
        if (_setDocumentBody == null)
            return false;

        IntPtr bodyUtf8 = Marshal.StringToCoTaskMemUTF8(bodyRml);
        try
        {
            return _setDocumentBody(document, bodyUtf8) != 0;
        }
        finally
        {
            Marshal.FreeCoTaskMem(bodyUtf8);
        }
    }

    private static bool TryExport<T>(IntPtr library, string exportName, out T export, out string error)
        where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(library, exportName, out IntPtr address))
        {
            export = null!;
            error = $"Missing native export '{exportName}'.";
            return false;
        }

        export = Marshal.GetDelegateForFunctionPointer<T>(address);
        error = "";
        return true;
    }

    [UnmanagedFunctionPointer(Convention)]
    private delegate int CreateContextFn(IntPtr contentRootUtf8, int width, int height, out IntPtr context);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void DestroyContextFn(IntPtr context);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void SetViewportFn(IntPtr context, int width, int height);

    [UnmanagedFunctionPointer(Convention)]
    private delegate int LoadDocumentFn(IntPtr context, IntPtr documentPathUtf8, out IntPtr document);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void ShowDocumentFn(IntPtr document);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void HideDocumentFn(IntPtr document);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void UpdateFn(IntPtr context, float deltaTime);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void RenderFn(IntPtr context);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void SetMousePositionFn(IntPtr context, float x, float y);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void SetMouseButtonFn(IntPtr context, int button, int down);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void SetKeyFn(IntPtr context, int key, int down);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void SubmitTextFn(IntPtr context, IntPtr textUtf8);

    [UnmanagedFunctionPointer(Convention)]
    private delegate int GetRenderDataFn(IntPtr context, out RmlUiRenderData renderData);

    [UnmanagedFunctionPointer(Convention)]
    private delegate void ReleaseRenderDataFn(IntPtr context);

    [UnmanagedFunctionPointer(Convention)]
    private delegate int SetDocumentBodyFn(IntPtr document, IntPtr bodyRmlUtf8);
}
