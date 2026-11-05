using System.Diagnostics;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class GpuFrameProfileChecks
{
    internal static void Run()
    {
        using var host = new EngineHost(800, 600, "GPU timing lifetime check");
        var device = host.Context.Renderer.GraphicsDevice;
        void Drain(GpuFrameProfile profile, int frame)
        {
            // Waiting is restricted to this standalone correctness test, never the profiler/runtime.
            device.WaitForIdle();
            var timeout = Stopwatch.StartNew();
            while (profile.Pending > 0)
            {
                profile.Poll(frame);
                if (timeout.Elapsed.TotalSeconds > 5) throw new TimeoutException("GPU test queries did not complete.");
                Thread.Sleep(1);
            }
        }
        using (var profile = new GpuFrameProfile(device, 32))
        {
            if (profile.DeviceFlags.Contains("Debug", StringComparison.Ordinal) != host.GraphicsDebug)
                throw new InvalidDataException("Graphics device validation policy not applied.");
            for (int frame = 0; frame < 17; frame++) { profile.Begin(frame); profile.End(); }
            if (profile.Pending != 16 || profile.Skipped != 1 || profile.Samples.Count != 0)
                throw new InvalidDataException("Full query ring did not skip without polling/waiting.");
            Drain(profile, 20);
            if (profile.Samples.Count != 16 || profile.Samples.Select(s => s.Frame).Distinct().Count() != 16 ||
                profile.Samples.Any(s => s.Frame < 0 || s.Frame >= 16 || s.Status != "ready" || !s.Milliseconds.HasValue || s.Milliseconds < 0))
                throw new InvalidDataException("GPU query identity/validity failed.");
            for (int frame = 20; frame < 36; frame++) { profile.Begin(frame); profile.End(); }
            Drain(profile, 40);
            for (int frame = 40; frame < 60; frame++) { profile.Begin(frame); profile.End(); }
            if (profile.Samples.Count != 32 || profile.Pending != 0)
                throw new InvalidDataException("Capture limit was not bounded.");
        }
        using (var profile = new GpuFrameProfile(device, 1))
        {
            profile.Begin(0);
            bool rejected = false;
            try { profile.Begin(1); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidDataException("Nested GPU sample accepted.");
            profile.End(); Drain(profile, 2);
        }
        // Disposing a pending sample must release the profiler's COM ownership without a GPU wait.
        using (var profile = new GpuFrameProfile(device, 1)) profile.Begin(0);
        device.WaitForIdle();
        var renderer = host.Context.Renderer;
        renderer.ConfigureGpuProfile(2);
        var previous = renderer.GpuProfile;
        renderer.ConfigureGpuProfile(0);
        if (renderer.GpuProfile != null) throw new InvalidDataException("Disabled capture retained GPU profiler.");
        previous!.Dispose();
        renderer.ConfigureGpuProfile(2);
        if (ReferenceEquals(previous, renderer.GpuProfile)) throw new InvalidDataException("New run reused an old capture.");
        renderer.ConfigureGpuProfile(0);
        Console.WriteLine("PASS: GPU ring saturation, frame identity, disjoint-checked results, reuse, capture bound and pending disposal.");
    }
}
