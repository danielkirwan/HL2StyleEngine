using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;

if (args is ["--self-test"])
{
    SchedulerAnalysis.SelfTest();
    return 0;
}
if (args.Length != 2)
{
    Console.Error.WriteLine("SchedulingProbe <recording.etl> <frames.json> | --self-test");
    return 2;
}
try { SchedulerAnalysis.Run(args[0], args[1]); return 0; }
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

internal static class SchedulerAnalysis
{
    // Raw QPC is intentional: these events join an independent engine clock on the same boot.
#pragma warning disable CS0618
    private static long Qpc(TraceEvent data) => data.TimeStampQPC;
#pragma warning restore CS0618
    private sealed record OffCpu(long Start, long End, long? Ready, string State, string Reason,
        int Cpu, int NextThread, int NextProcess, string NextProcessName);
    private sealed record SpanResult(double OffCpuMs, double ReadyMs, double WaitingMs, double UnknownMs);

    private static SpanResult Intersect(OffCpu span, long start, long end, long frequency)
    {
        double Ms(long a, long b) => Math.Max(0, Math.Min(end, b) - Math.Max(start, a)) * 1000.0 / frequency;
        double total = Ms(span.Start, span.End);
        if (span.State is "Ready" or "Standby") return new(total, total, 0, 0);
        if (span.Ready is { } ready && ready >= span.Start && ready <= span.End)
            return new(total, Ms(ready, span.End), Ms(span.Start, ready), 0);
        return new(total, 0, 0, total);
    }

    internal static void SelfTest()
    {
        var wait = new OffCpu(10, 30, 22, "Wait", "Executive", 0, 1, 2, "test");
        if (Intersect(wait, 15, 25, 1000) != new SpanResult(10, 3, 7, 0) ||
            Intersect(wait with { State = "Ready" }, 15, 25, 1000) != new SpanResult(10, 10, 0, 0) ||
            Intersect(wait with { Ready = null }, 15, 25, 1000) != new SpanResult(10, 0, 0, 10) ||
            Intersect(wait, 31, 40, 1000) != new SpanResult(0, 0, 0, 0) ||
            Intersect(wait, 10, 30, 2000) != new SpanResult(10, 4, 6, 0))
            throw new InvalidDataException("Clipping/ready/wait/unknown/frequency regression.");
        Console.WriteLine("PASS: scheduling interval clipping, ready/wait attribution, unknown gaps and QPC units.");
    }

    internal static void Run(string tracePath, string framesPath)
    {
        using var framesDocument = JsonDocument.Parse(File.ReadAllText(framesPath));
        var root = framesDocument.RootElement;
        if (root.GetProperty("Version").GetInt32() < 5) throw new InvalidDataException("Version 5 QPC frame report required.");
        int process = root.GetProperty("ProcessId").GetInt32(), thread = root.GetProperty("NativeThreadId").GetInt32();
        long frequency = root.GetProperty("QpcFrequency").GetInt64();
        if (process <= 0 || thread <= 0 || frequency <= 0) throw new InvalidDataException("Missing native clock identity.");
        var frames = root.GetProperty("Frames").EnumerateArray().Where(f => !f.GetProperty("Warmup").GetBoolean()).ToArray();
        var phases = new Dictionary<int, string>();
        string summaryPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(framesPath))!, "summary.json");
        if (File.Exists(summaryPath))
        {
            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            if (summary.RootElement.TryGetProperty("Intervals", out var intervals))
                foreach (var interval in intervals.EnumerateArray())
                    phases[interval.GetProperty("Frame").GetInt32()] = interval.GetProperty("Phase").GetString()!;
        }

        var spans = new List<OffCpu>();
        OffCpu? pending = null;
        long first = long.MaxValue, last = 0;
        int matchedSwitches = 0, hardFaults = 0;
        using var source = new ETWTraceEventSource(tracePath);
        source.Kernel.ThreadCSwitch += data =>
        {
            long timestamp = Qpc(data);
            first = Math.Min(first, timestamp); last = Math.Max(last, timestamp);
            if (data.NewThreadID == thread && data.NewProcessID == process)
            {
                matchedSwitches++;
                if (pending != null && timestamp >= pending.Start)
                    spans.Add(pending with { End = timestamp });
                pending = null;
            }
            if (data.OldThreadID == thread && data.OldProcessID == process)
                pending = new(timestamp, 0, null, data.OldThreadState.ToString(), data.OldThreadWaitReason.ToString(),
                    data.ProcessorNumber, data.NewThreadID, data.NewProcessID, data.NewProcessName);
        };
        source.Kernel.DispatcherReadyThread += data =>
        {
            if (data.AwakenedThreadID == thread && pending != null && pending.Ready == null)
                pending = pending with { Ready = Qpc(data) };
        };
        source.Kernel.MemoryHardFault += data => { if (data.ThreadID == thread) hardFaults++; };
        source.Process();
        if (matchedSwitches == 0) throw new InvalidDataException("No matching process/thread switches; trace cannot support attribution.");
        object Window(long start, long end)
        {
            var matches = spans.Where(s => s.Start < end && s.End > start).ToArray();
            var parts = matches.Select(s => Intersect(s, start, end, frequency)).ToArray();
            return new
            {
                WallMs = (end - start) * 1000.0 / frequency,
                OffCpuMs = parts.Sum(p => p.OffCpuMs), ReadyMs = parts.Sum(p => p.ReadyMs),
                WaitingMs = parts.Sum(p => p.WaitingMs), UnclassifiedOffCpuMs = parts.Sum(p => p.UnknownMs),
                LongestSwitchOuts = matches.OrderByDescending(s => Intersect(s, start, end, frequency).OffCpuMs).Take(5)
                    .Select(s => new { s.Start, s.End, s.Ready, s.State, s.Reason, s.Cpu, s.NextThread, s.NextProcess,
                        s.NextProcessName, Clipped = Intersect(s, start, end, frequency) })
            };
        }
        var selected = frames.OrderByDescending(f => f.GetProperty("Milliseconds").EnumerateArray().Sum(v => v.GetDouble())).Take(16)
            .Concat(frames.Where(f => phases.GetValueOrDefault(f.GetProperty("Frame").GetInt32()) == "gameplay")
                .OrderByDescending(f => f.GetProperty("Milliseconds").EnumerateArray().Sum(v => v.GetDouble())).Take(12))
            .Concat(frames.OrderByDescending(f => f.GetProperty("Milliseconds")[1].GetDouble()).Take(12))
            .DistinctBy(f => f.GetProperty("Frame").GetInt32()).ToArray();
        var report = new
        {
            Trace = Path.GetFullPath(tracePath), Frames = Path.GetFullPath(framesPath), ProcessId = process, NativeThreadId = thread,
            QpcFrequency = frequency, source.EventsLost, FirstSwitchQpc = first, LastSwitchQpc = last, MatchedSwitches = matchedSwitches,
            MainThreadHardFaultEvents = hardFaults,
            SelectedFrames = selected.Select(f =>
            {
                int id = f.GetProperty("Frame").GetInt32();
                long start = f.GetProperty("HostTimestampQpc").GetInt64(), end = f.GetProperty("EndTimestampQpc").GetInt64();
                var stages = f.GetProperty("Milliseconds").EnumerateArray().Select(v => v.GetDouble()).ToArray();
                long physicsStart = start + (long)Math.Round(stages[0] * frequency / 1000);
                long physicsEnd = physicsStart + (long)Math.Round(stages[1] * frequency / 1000);
                return new { Frame = id, Phase = phases.GetValueOrDefault(id, "unclassified"),
                    Covered = first <= start && last >= end && source.EventsLost == 0,
                    Host = Window(start, end), Physics = Window(physicsStart, physicsEnd),
                    GcPauseMs = f.GetProperty("GcPauseMilliseconds").GetDouble(), Focused = f.GetProperty("WindowFocused").GetBoolean() };
            }),
            Notes = "Diagnostic trace, not an acceptance substitute. QPC matches the same machine/boot. Ready is descheduling, waiting ends at ReadyThread, otherwise unclassified. Next process is only the immediate switch-in, not proof of the delay's cause. Running spans can include interrupts. No stack/driver attribution is inferred. Lost events or incomplete coverage prohibit conclusive attribution."
        };
        string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(tracePath))!, "scheduling-analysis.json");
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[Scheduling] {matchedSwitches} thread switch-ins; lost={source.EventsLost}; {path}");
    }
}
