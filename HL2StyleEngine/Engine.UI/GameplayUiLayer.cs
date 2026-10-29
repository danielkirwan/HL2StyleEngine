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
    public UiWorkProfile? Profile
    {
        get => _rmlUiBackend.Profile;
        set { _rmlUiBackend.Profile = value; _previewRenderer.Profile = value; }
    }
    public Func<string, UiImage>? IconResolver { set => _previewRenderer.IconResolver = value; }
    public string Status => BuildStatus();
    public string RenderStatus => _rmlUiBackend.RenderStatus;
    public bool IsReady => _rmlUiBackend.IsReady;
    public bool UsesNativePresentation => _nativePresentationEnabled && _rmlUiBackend.IsReady;
    public bool NativeFrameVisible => UsesNativePresentation && _rmlUiBackend.HasSubmittedOverlayFrame;
    public bool UsesPreviewPresentation => !NativeFrameVisible;
    private bool NeedsNativeDocument => (_nativePresentationEnabled && _rmlUiBackend.HasNativeApi) || _rmlUiBackend.TestDocumentEnabled;

    public static GameplayUiLayer CreateRmlUi(string contentRoot)
        => new(RmlUiBackend.Probe(contentRoot), NativePresentationOptedIn());

    public void Update(RmlUiFrameContext context)
    {
        if (!NeedsNativeDocument) return;
        _rmlUiBackend.Update(context);
        if (_latestState.LoadingOverlayVisible && context.Renderer != null)
            _rmlUiBackend.PrewarmOverlay(context.Renderer);
    }

    public void SubmitState(GameplayUiState state)
    {
        _latestState = state;
        if (NeedsNativeDocument) _rmlUiBackend.SubmitState(state);
    }

    public void QueuePreloadIcon(string absolutePath, long bytes)
    {
        if (_nativePresentationEnabled && _rmlUiBackend.HasNativeApi) _rmlUiBackend.QueuePreloadIcon(absolutePath, bytes);
    }

    public bool DrawPreview(out int selectedSlot)
    {
        selectedSlot = -1;
        if (UsesNativePresentation && !ShouldForcePreviewForState(_latestState))
            return false;

        if (NativeFrameVisible && !ShouldForcePreviewForState(_latestState))
            return false;

        using var measure = Profile?.Measure("preview-draw");
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
                ? " Gameplay overlay is using the stable ImGui preview renderer while native RmlUi presentation is still being validated."
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
        => (_forcePreviewModals && state.ItemCollectedOpen) ||
           state.HudVisible ||
           state.AmmoHudVisible ||
           state.WeaponSelectorVisible ||
           state.LoadingOverlayVisible;

}
