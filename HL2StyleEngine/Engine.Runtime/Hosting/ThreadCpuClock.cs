using System.Runtime.InteropServices;

namespace Engine.Runtime.Hosting;

// Diagnostic-only counters. CPU time is quantized; cycles are never converted to time.
public static class ThreadCpuClock
{
    internal static int NativeThreadId => OperatingSystem.IsWindows() ? unchecked((int)GetCurrentThreadId()) : 0;
    public readonly record struct Sample(int ThreadId, ulong Ticks, ulong Cycles);
    public readonly record struct Delta(double Milliseconds, ulong Cycles);

    public static Sample? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        nint thread = GetCurrentThread();
        if (!GetThreadTimes(thread, out _, out _, out var kernel, out var user) ||
            !QueryThreadCycleTime(thread, out ulong cycles)) return null;
        return new(Environment.CurrentManagedThreadId, kernel.Ticks + user.Ticks, cycles);
    }

    public static Delta? Elapsed(Sample? start, Sample? end)
    {
        if (start is not { } a || end is not { } b || a.ThreadId != b.ThreadId ||
            b.Ticks < a.Ticks || b.Cycles < a.Cycles) return null;
        return new((b.Ticks - a.Ticks) / 10000.0, b.Cycles - a.Cycles);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low, High;
        public readonly ulong Ticks => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(nint thread, out FileTime creation, out FileTime exit,
        out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);
}
