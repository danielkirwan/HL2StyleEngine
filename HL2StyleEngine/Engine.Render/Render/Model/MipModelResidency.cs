namespace Engine.Render;

// Experimental model-granularity mip replacement. Request/Pump/Retire are render-thread only;
// the supplied loader owns immutable CPU inputs and never touches graphics resources.
public sealed class MipModelResidency(Func<int, LoadedModel> load)
{
    private Task<LoadedModel>? _job;
    private LoadedModel? _prepared;
    private int _pendingMip, _failedMip = -1;
    private bool _retired;
    public RenderModel? Model { get; private set; }
    public int CurrentMip { get; private set; } = -1;
    public int RequestedMip { get; private set; } = 3;
    public int Replacements { get; private set; }
    public bool Busy => _job != null || _prepared != null;
    public string Error { get; private set; } = "";

    public void Request(int mip)
    {
        if (mip < 0 || mip > 14) throw new ArgumentOutOfRangeException(nameof(mip));
        if (RequestedMip != mip) { _failedMip = -1; Error = ""; }
        RequestedMip = mip;
    }

    public void Retry() { _failedMip = -1; Error = ""; }

    // Callers bound concurrency and transient memory by admitting only one replacement
    // until its retired resources have cleared the renderer's fence queue.
    public void Pump(BasicWorldRenderer world, Renderer renderer)
    {
        if (_retired) return;
        if (!Busy && CurrentMip != RequestedMip && _failedMip != RequestedMip)
        {
            _pendingMip = RequestedMip;
            int mip = _pendingMip;
            _job = Task.Run(() => load(mip));
        }
        try
        {
            if (_job is { IsCompleted: true })
            {
                _prepared = _job.GetAwaiter().GetResult();
                _job = null;
            }
            if (_prepared == null) return;
            if (_pendingMip != RequestedMip)
            {
                world.RetirePendingModel(_prepared, renderer);
                _prepared = null;
                return;
            }
            if (!world.TryCreateRenderModel(_prepared, out var replacement)) return;
            if (Model != null) { renderer.RetireAfterFrame(Model); Replacements++; }
            Model = replacement;
            CurrentMip = _pendingMip;
            _prepared = null;
            Error = "";
        }
        catch (Exception ex)
        {
            if (_prepared != null) world.RetirePendingModel(_prepared, renderer);
            _prepared = null; _job = null;
            _failedMip = _pendingMip == RequestedMip ? _pendingMip : -1;
            Error = _pendingMip == RequestedMip ? ex.Message : "";
        }
    }

    public void Retire(BasicWorldRenderer world, Renderer renderer)
    {
        _retired = true;
        if (Model != null) renderer.RetireAfterFrame(Model);
        if (_prepared != null) world.RetirePendingModel(_prepared, renderer);
        if (_job != null) _ = _job.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        Model = null; _prepared = null; _job = null;
    }
}
