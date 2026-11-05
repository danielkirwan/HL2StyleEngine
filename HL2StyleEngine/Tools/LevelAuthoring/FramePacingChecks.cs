using System.Text.Json;

internal static class FramePacingChecks
{
    internal static void Run(string root, string[] args)
    {
        string[] keys = ["HS2_GPU_PROFILE", "HS2_PROFILE_DEBUG", "HS2_PROFILE_VSYNC"];
        var old = keys.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            Environment.SetEnvironmentVariable(keys[0], "1");
            Environment.SetEnvironmentVariable(keys[1], args.Contains("--no-debug") ? "0" : "1");
            Environment.SetEnvironmentVariable(keys[2], args.Contains("--no-vsync") ? "0" : "1");
            if (args.Contains("--long"))
                GameplaySoakChecks.Run(root, native: true, cycles: 18, textureTrial: true, textureReference: args.Contains("--reference"));
            else TextureStreamingTrial.Run(root, automatic: true, reference: args.Contains("--reference"));
        }
        finally { for (int i = 0; i < keys.Length; i++) Environment.SetEnvironmentVariable(keys[i], old[i]); }
    }

    internal static void Analyze(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("Version").GetInt32() < 3) throw new InvalidDataException("Use a version 3 or newer frame trace.");
        var frames = root.GetProperty("Frames").EnumerateArray().Where(f => !f.GetProperty("Warmup").GetBoolean()).ToArray();
        string[] renderNames = root.GetProperty("RenderStages").Deserialize<string[]>()!;
        string[] physicsNames = root.GetProperty("PhysicsStages").Deserialize<string[]>()!;
        var gpu = root.GetProperty("GpuFrames");
        var samples = gpu.ValueKind == JsonValueKind.Array ? gpu.EnumerateArray().ToArray() : [];
        var gpuByFrame = samples.ToDictionary(s => s.GetProperty("Frame").GetInt32());
        var phases = new Dictionary<int, string>();
        string summaryPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "summary.json");
        if (File.Exists(summaryPath))
        {
            using var summary = JsonDocument.Parse(File.ReadAllText(summaryPath));
            if (summary.RootElement.TryGetProperty("Intervals", out var intervals))
                foreach (var interval in intervals.EnumerateArray())
                    phases[interval.GetProperty("Frame").GetInt32()] = interval.GetProperty("Phase").GetString()!;
        }
        double[] Values(JsonElement frame, string name) => frame.GetProperty(name).Deserialize<double[]>()!;
        object Stats(IEnumerable<double> source)
        {
            var sorted = source.Order().ToArray();
            return new { Count = sorted.Length, Mean = sorted.DefaultIfEmpty().Average(),
                P95 = sorted.Length == 0 ? 0 : sorted[(int)((sorted.Length - 1) * .95)], Max = sorted.LastOrDefault() };
        }
        var rows = frames.Select(f =>
        {
            int frame = f.GetProperty("Frame").GetInt32();
            double[] host = Values(f, "Milliseconds"), render = Values(f, "RenderMilliseconds"), physics = Values(f, "PhysicsMilliseconds");
            int worst = Array.IndexOf(physics, physics.Max());
            double? gpuMs = gpuByFrame.TryGetValue(frame, out var sample) && sample.GetProperty("Milliseconds").ValueKind == JsonValueKind.Number
                ? sample.GetProperty("Milliseconds").GetDouble() : null;
            return new { Frame = frame, ProfiledHostMs = host.Sum(), PhysicsMs = host[1],
                ThreadCpuMs = f.TryGetProperty("MainThreadCpuMilliseconds", out var cpu) && cpu.ValueKind == JsonValueKind.Number ? (double?)cpu.GetDouble() : null,
                PhysicsCpuMs = f.TryGetProperty("ThreadCpuMilliseconds", out var cpuStages) && cpuStages[1].ValueKind == JsonValueKind.Number ? (double?)cpuStages[1].GetDouble() : null,
                PhysicsCycles = f.TryGetProperty("ThreadCycles", out var cycles) && cycles[1].ValueKind == JsonValueKind.Number ? (ulong?)cycles[1].GetUInt64() : null,
                Phase = phases.GetValueOrDefault(frame, "unclassified"),
                Focused = f.TryGetProperty("WindowFocused", out var focus) ? (bool?)focus.GetBoolean() : null,
                FixedSteps = f.GetProperty("FixedSteps").GetInt32(), PreviousFrameMs = f.GetProperty("PreviousFrameIntervalMilliseconds").GetDouble(),
                PhysicsStage = physicsNames[worst], PhysicsStageMs = physics[worst],
                PresentMs = render[Array.IndexOf(renderNames, "Present")], GpuCommandsMs = gpuMs,
                GcPauseMs = f.GetProperty("GcPauseMilliseconds").GetDouble() };
        }).ToArray();
        var report = new
        {
            Source = Path.GetFullPath(path), GraphicsDebugRequested = root.GetProperty("GraphicsDebugRequested"), VSync = root.GetProperty("VSync"),
            DeviceFlags = root.GetProperty("GpuDeviceFlags").GetString(), MeasuredFrames = frames.Length,
            GpuValid = rows.Count(r => r.GpuCommandsMs.HasValue), GpuInvalid = samples.Count(s => s.GetProperty("Milliseconds").ValueKind == JsonValueKind.Null),
            GpuPending = root.GetProperty("GpuPending"), GpuSkipped = root.GetProperty("GpuSkipped"),
            GpuCommands = Stats(rows.Where(r => r.GpuCommandsMs.HasValue).Select(r => r.GpuCommandsMs!.Value)),
            Present = Stats(rows.Select(r => r.PresentMs)), Physics = Stats(rows.Select(r => r.PhysicsMs)),
            PhysicsPerFixedStep = Stats(rows.Where(r => r.FixedSteps > 0).Select(r => r.PhysicsMs / r.FixedSteps)),
            ThreadCpu = Stats(rows.Where(r => r.ThreadCpuMs.HasValue).Select(r => r.ThreadCpuMs!.Value)),
            PhysicsCpu = Stats(rows.Where(r => r.PhysicsCpuMs.HasValue).Select(r => r.PhysicsCpuMs!.Value)),
            CatchUpFrames = rows.Count(r => r.FixedSteps > 1), WorstFrames = rows.OrderByDescending(r => r.ProfiledHostMs).Take(12),
            GameplayFrames = rows.Count(r => r.Phase == "gameplay"), LoadingFrames = rows.Count(r => r.Phase == "loading"),
            WorstGameplayFrames = rows.Where(r => r.Phase == "gameplay").OrderByDescending(r => r.ProfiledHostMs).Take(6),
            Notes = "Host work excludes inter-frame gaps. GPU timestamps cover submitted commands, not earlier immediate uploads or display latency. Do not subtract GPU time from Present or infer causality from correlation. Thread CPU time is quantized; cycles are not time. Original route summaries retain acceptance gates."
        };
        string output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "pacing-analysis.json");
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(output, json);
        Console.WriteLine(json);
        Console.WriteLine("[FramePacing] " + output);
    }
}
