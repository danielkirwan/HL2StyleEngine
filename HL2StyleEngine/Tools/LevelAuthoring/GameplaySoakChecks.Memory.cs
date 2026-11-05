using System.Diagnostics;

internal sealed partial class GameplaySoakChecks
{
    private bool _noReload, _memoryStages, _assetsObserved, _lightingObserved;
    private bool _heapSnapshots;
    private int _requestedCycles;
    private string _diagnosticTool = "";
    private long _observerAllocated;
    private readonly List<object> _stageSamples = new(512);
    private readonly HashSet<string> _sampledStages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StageAllocation> _stageAllocations = new(StringComparer.Ordinal);
    private sealed class StageAllocation
    {
        public long Start, Calls, Bytes, Maximum, LoadingBytes, GameplayBytes;
    }

    private void EnableMemoryStages()
    {
        _memoryStages = true;
        _game.MemoryDiagnosticStage = ObserveStage;
    }

    private void ObserveStage(string name, bool begin)
    {
        long observerStart = GC.GetAllocatedBytesForCurrentThread();
        if (!_stageAllocations.TryGetValue(name, out var stage))
            _stageAllocations.Add(name, stage = new());
        if (begin)
        {
            _observerAllocated += GC.GetAllocatedBytesForCurrentThread() - observerStart;
            stage.Start = GC.GetAllocatedBytesForCurrentThread();
            return;
        }
        long bytes = observerStart - stage.Start;
        stage.Calls++; stage.Bytes += bytes; stage.Maximum = Math.Max(stage.Maximum, bytes);
        if (_loading) stage.LoadingBytes += bytes; else stage.GameplayBytes += bytes;
        // Process sampling is limited to the first call per load stage. The counters
        // above cover every call, but only managed allocations on the calling thread.
        if (_loading && _sampledStages.Add(name)) SampleMemoryStage(name + "-first-call");
        _observerAllocated += GC.GetAllocatedBytesForCurrentThread() - observerStart;
    }

    private object[] StageAllocationReport() => _stageAllocations.Select(p => (object)new
    {
        Stage = p.Key, p.Value.Calls, p.Value.Bytes, p.Value.Maximum, p.Value.LoadingBytes, p.Value.GameplayBytes
    }).ToArray();

    private void SampleMemoryStage(string stage)
    {
        if (!_memoryStages) return;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var gc = GC.GetGCMemoryInfo();
        _stageSamples.Add(new
        {
            Stage = stage, Frame = _frame, Cycle = _completedCycles, Transition = _transitions,
            PrivateBytes = process.PrivateMemorySize64, WorkingSet = process.WorkingSet64,
            ManagedBytes = GC.GetTotalMemory(false), gc.Index, gc.HeapSizeBytes, gc.FragmentedBytes,
            gc.TotalCommittedBytes, TotalAllocatedBytes = GC.GetTotalAllocatedBytes(false),
            Collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
            Generations = gc.GenerationInfo.ToArray().Select((g, i) => new
            { Generation = i, g.SizeAfterBytes, g.FragmentationAfterBytes }).ToArray(),
            ModelBytes = _game.StreamingResidentBytes, Models = _game.StreamingResidentModels,
            DiagnosticSamples = _stageSamples.Count, IntervalSamples = _intervals.Count,
            WeakReferences = _oldEntities.Count + _oldMeshes.Count
        });
    }

    private void CaptureHeapSnapshot()
    {
        if (!File.Exists(_diagnosticTool)) throw new FileNotFoundException("Install dotnet-gcdump in .dotnet/diagnostics for this separate diagnostic run.", _diagnosticTool);
        string prefix = Path.Combine(_folder, _completedCycles == 1 ? "heap-early" : "heap-late");
        var start = new ProcessStartInfo(_diagnosticTool)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "collect", "--process-id", Environment.ProcessId.ToString(), "--output", prefix + ".gcdump", "--timeout", "90" }) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_diagnosticTool)!, ".."));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start heap collector.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Heap collector timed out."); }
        File.WriteAllText(prefix + ".log", output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        if (process.ExitCode != 0) throw new InvalidDataException("Heap collector failed: " + prefix + ".log");
        SampleMemoryStage("after-diagnostic-heap-collection");
        Console.WriteLine("[Memory] Captured " + prefix + ".gcdump (forced collection; NOT performance acceptance)");
    }
}
