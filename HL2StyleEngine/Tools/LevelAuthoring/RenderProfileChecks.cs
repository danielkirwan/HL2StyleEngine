using Engine.Render;

internal static class RenderProfileChecks
{
    internal static void Run()
    {
        var profile = new RenderCpuProfile();
        void Measure() { using var scope = profile.Measure(RenderWork.ObjectUploads); }
        for (int i = 0; i < 100; i++) Measure();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Measure();
        if (GC.GetAllocatedBytesForCurrentThread() != before || profile.Snapshot().Any(x => x != 0))
            throw new InvalidDataException("Disabled render diagnostics allocated or recorded work.");
        profile.Enabled = true;
        using (profile.Measure(RenderWork.ObjectUploads)) Thread.SpinWait(1000);
        var sample = profile.Snapshot();
        if (sample.Length != RenderCpuProfile.Stages.Length || sample[(int)RenderWork.ObjectUploads] <= 0 ||
            sample.Where((_, i) => i != (int)RenderWork.ObjectUploads).Any(x => x != 0))
            throw new InvalidDataException("Render stage attribution failed.");
        profile.Reset();
        if (profile.Snapshot().Any(x => x != 0) || sample[(int)RenderWork.ObjectUploads] <= 0)
            throw new InvalidDataException("Frame reset changed a previously captured snapshot.");
        var physics = new Engine.Physics.PhysicsCpuProfile();
        void MeasurePhysics() { using var scope = physics.Measure(Engine.Physics.PhysicsWork.Player); }
        for (int i = 0; i < 100; i++) MeasurePhysics();
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) MeasurePhysics();
        if (GC.GetAllocatedBytesForCurrentThread() != before || physics.Snapshot().Any(x => x != 0) || physics.Calls().Any(x => x != 0))
            throw new InvalidDataException("Disabled physics diagnostics allocated or recorded work.");
        physics.Enabled = true;
        using (physics.Measure(Engine.Physics.PhysicsWork.Player)) Thread.SpinWait(1000);
        var physicsSample = physics.Snapshot(); var calls = physics.Calls();
        if (physicsSample[(int)Engine.Physics.PhysicsWork.Player] <= 0 || calls[(int)Engine.Physics.PhysicsWork.Player] != 1)
            throw new InvalidDataException("Physics stage attribution failed.");
        physics.Reset();
        if (physics.Calls().Any(x => x != 0) || physics.Snapshot().Any(x => x != 0) || physicsSample[(int)Engine.Physics.PhysicsWork.Player] <= 0)
            throw new InvalidDataException("Physics reset/snapshot failed.");
        var fixedTime = new Engine.Core.Time.FixedTimestep();
        float step = Engine.Core.Time.Time.FixedDeltaTime;
        float oldTotal = Engine.Core.Time.Time.TotalTime;
        try
        {
            int ticks = 0;
            fixedTime.Update(step * .5f, () => ticks++);
            if (ticks != 0 || fixedTime.LastStepCount != 0) throw new InvalidDataException("Fractional tick changed.");
            fixedTime.Update(step * 3, () => ticks++);
            if (ticks != 3 || fixedTime.LastStepCount != 3 || MathF.Abs(fixedTime.AccumulatorSeconds - step * .5f) > .00001f)
                throw new InvalidDataException("Catch-up tick accounting changed simulation.");
            fixedTime.Update(0, () => ticks++);
            if (ticks != 3 || fixedTime.LastStepCount != 0) throw new InvalidDataException("Tick count not reset.");
        }
        finally { Engine.Core.Time.Time.TotalTime = oldTotal; }
        CheckThreadClock();
        Console.WriteLine("PASS: render/physics disabled allocation-free scopes, attribution, reset/snapshots, fixed-tick accounting and thread CPU counters.");
    }

    private static void CheckThreadClock()
    {
        var start = Engine.Runtime.Hosting.ThreadCpuClock.Read();
        if (!OperatingSystem.IsWindows())
        {
            if (start != null) throw new InvalidDataException("Unsupported thread clock must be unavailable.");
            return;
        }
        if (start == null) throw new InvalidDataException("Windows thread CPU counters unavailable.");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Thread.Sleep(100);
        var afterSleep = Engine.Runtime.Hosting.ThreadCpuClock.Read();
        var sleep = Engine.Runtime.Hosting.ThreadCpuClock.Elapsed(start, afterSleep);
        if (sleep == null || sleep.Value.Milliseconds >= watch.Elapsed.TotalMilliseconds)
            throw new InvalidDataException("Sleeping thread recorded its entire wall time as CPU work.");
        watch.Restart();
        while (watch.ElapsedMilliseconds < 100) Thread.SpinWait(1000);
        var work = Engine.Runtime.Hosting.ThreadCpuClock.Elapsed(afterSleep, Engine.Runtime.Hosting.ThreadCpuClock.Read());
        if (work == null || work.Value.Cycles == 0)
            throw new InvalidDataException("CPU work did not increment thread cycles.");
        var a = new Engine.Runtime.Hosting.ThreadCpuClock.Sample(1, 10000, 100);
        var b = new Engine.Runtime.Hosting.ThreadCpuClock.Sample(1, 30000, 250);
        var known = Engine.Runtime.Hosting.ThreadCpuClock.Elapsed(a, b);
        if (known?.Milliseconds != 2 || known?.Cycles != 150 ||
            Engine.Runtime.Hosting.ThreadCpuClock.Elapsed(null, b) != null ||
            Engine.Runtime.Hosting.ThreadCpuClock.Elapsed(b, a) != null ||
            Engine.Runtime.Hosting.ThreadCpuClock.Elapsed(a, b with { ThreadId = 2 }) != null)
            throw new InvalidDataException("CPU counter difference validation failed.");
        Console.WriteLine($"[ThreadClock] sleep CPU {sleep.Value.Milliseconds:F3} ms; work CPU {work.Value.Milliseconds:F3} ms / {work.Value.Cycles} cycles.");
    }
}
