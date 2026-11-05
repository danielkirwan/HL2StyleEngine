namespace Game;

public sealed partial class HL2GameModule
{
    // Opt-in QA observer. No sampling, retained data or forced collection in normal play.
    public Action<string, bool>? MemoryDiagnosticStage { get; set; }

    private MemoryStage MeasureMemoryStage(string name) => new(MemoryDiagnosticStage, name);

    private readonly struct MemoryStage : IDisposable
    {
        private readonly Action<string, bool>? _observer;
        private readonly string _name;
        public MemoryStage(Action<string, bool>? observer, string name)
        {
            _observer = observer;
            _name = name;
            observer?.Invoke(name, true);
        }
        public void Dispose() => _observer?.Invoke(_name, false);
    }
}
