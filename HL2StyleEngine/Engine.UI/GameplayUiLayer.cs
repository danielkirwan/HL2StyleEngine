using Engine.Render;

namespace Engine.UI;

public sealed class GameplayUiLayer : IDisposable
{
    private const string NativePresentationEnvironmentVariable = "HS2_RMLUI_NATIVE_PRESENTATION";
    private const string ForcePreviewModalsEnvironmentVariable = "HS2_RMLUI_FORCE_PREVIEW_MODALS";

    private readonly RmlUiBackend _rmlUiBackend;
    private readonly GameplayUiImGuiPreviewRenderer _previewRenderer = new();
    private readonly bool _nativePresentationEnabled;
    private readonly bool _forcePreviewModals;
    private GameplayUiState _latestState = new();

    public GameplayUiLayer(RmlUiBackend rmlUiBackend, bool nativePresentationEnabled)
    {
        _rmlUiBackend = rmlUiBackend;
        _nativePresentationEnabled = nativePresentationEnabled;
        _forcePreviewModals = EnvironmentFlagEnabled(ForcePreviewModalsEnvironmentVariable);
    }

    public string BackendName => _rmlUiBackend.Name;
    public string Status => BuildStatus();
    public string RenderStatus => _rmlUiBackend.RenderStatus;
    public bool IsReady => _rmlUiBackend.IsReady;
    public bool UsesNativePresentation => _nativePresentationEnabled && _rmlUiBackend.IsReady;
    public bool NativeFrameVisible => UsesNativePresentation && _rmlUiBackend.HasSubmittedOverlayFrame;
    public bool UsesPreviewPresentation => !NativeFrameVisible;

    public static GameplayUiLayer CreateRmlUi(string contentRoot)
        => new(RmlUiBackend.Probe(contentRoot), NativePresentationOptedIn());

    public void Update(RmlUiFrameContext context)
    {
        _rmlUiBackend.Update(context);
    }

    public void SubmitState(GameplayUiState state)
    {
        _latestState = state;
        _rmlUiBackend.SubmitState(state);
    }

    public bool DrawPreview(out int selectedSlot)
    {
        selectedSlot = -1;
        if (NativeFrameVisible && !ShouldForcePreviewForState(_latestState))
            return false;

        return _previewRenderer.Draw(_latestState, out selectedSlot);
    }

    public bool TryGetNativeHoveredSlot(out int slot)
    {
        slot = -1;
        return NativeFrameVisible && _rmlUiBackend.TryGetHoveredDataSlot(out slot);
    }

    public void RenderOverlay(Renderer renderer)
    {
        if (!_nativePresentationEnabled)
            return;

        if (ShouldForcePreviewForState(_latestState))
            return;

        _rmlUiBackend.RenderOverlay(renderer);
    }

    public void Dispose()
    {
        _rmlUiBackend.Dispose();
    }

    private string BuildStatus()
    {
        if (_nativePresentationEnabled)
        {
            string testNote = _rmlUiBackend.TestDocumentEnabled
                ? " Diagnostic test document is enabled."
                : "";
            string forcedPreviewNote = ShouldForcePreviewForState(_latestState)
                ? " Critical pickup/examine modal is using the stable preview renderer because HS2_RMLUI_FORCE_PREVIEW_MODALS is enabled."
                : "";
            string frameNote = _rmlUiBackend.HasSubmittedOverlayFrame
                ? $" {_rmlUiBackend.OverlayDebugStatus}"
                : " Native overlay has not submitted a visible frame yet; ImGui preview remains as a safety net.";
            return $"{_rmlUiBackend.Status}{testNote}{forcedPreviewNote}{frameNote}";
        }

        if (_rmlUiBackend.NativeBridgeFound)
            return $"{_rmlUiBackend.Status} Native presentation is opt-in while the bridge is validated; ImGui gameplay preview remains active.";

        return _rmlUiBackend.Status;
    }

    private static bool NativePresentationOptedIn()
        => EnvironmentFlagEnabled(NativePresentationEnvironmentVariable);

    private static bool EnvironmentFlagEnabled(string variableName)
    {
        string? value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldForcePreviewForState(GameplayUiState state)
        => _forcePreviewModals && state.ItemCollectedOpen;
}
