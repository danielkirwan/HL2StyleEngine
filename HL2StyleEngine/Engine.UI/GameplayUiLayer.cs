using Engine.Render;

namespace Engine.UI;

public sealed class GameplayUiLayer : IDisposable
{
    private readonly RmlUiBackend _rmlUiBackend;
    private readonly GameplayUiImGuiPreviewRenderer _previewRenderer = new();
    private GameplayUiState _latestState = new();

    public GameplayUiLayer(RmlUiBackend rmlUiBackend)
    {
        _rmlUiBackend = rmlUiBackend;
    }

    public string BackendName => _rmlUiBackend.Name;
    public string Status => _rmlUiBackend.Status;
    public string RenderStatus => _rmlUiBackend.RenderStatus;
    public bool IsReady => _rmlUiBackend.IsReady;
    public bool UsesNativePresentation => _rmlUiBackend.IsReady;
    public bool UsesPreviewPresentation => !UsesNativePresentation;

    public static GameplayUiLayer CreateRmlUi(string contentRoot)
        => new(RmlUiBackend.Probe(contentRoot));

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
        if (UsesNativePresentation)
            return false;

        return _previewRenderer.Draw(_latestState, out selectedSlot);
    }

    public void RenderOverlay(Renderer renderer)
    {
        _rmlUiBackend.RenderOverlay(renderer);
    }

    public void Dispose()
    {
        _rmlUiBackend.Dispose();
    }
}
