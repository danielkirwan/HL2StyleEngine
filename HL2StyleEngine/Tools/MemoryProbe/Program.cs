using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

try
{
    if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        throw new PlatformNotSupportedException("This address-space diagnostic requires Windows x64.");
    if (args is ["capture", var pid, var output])
        MemoryCapture.Write(int.Parse(pid), output);
    else if (args is ["self-test"])
        ProbeTests.Run();
    else if (args is ["fixture"])
        ProbeTests.Fixture();
    else throw new ArgumentException("MemoryProbe capture <owned-test-pid> <output.json> | self-test");
}
catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }

internal readonly record struct OwnedRange(ulong Start, ulong End, string Kind);
internal readonly record struct Region(ulong Start, ulong End, ulong AllocationBase, uint State, uint Type, uint Protect);

internal static class MemoryCapture
{
    internal static void Write(int pid, string output)
    {
        if (pid == Environment.ProcessId) throw new ArgumentException("Use a separate owned test process.");
        var watch = Stopwatch.StartNew();
        object report;
        // The target is paused during both CLR and OS inspection, and always resumed
        // by DataTarget.Dispose. No collection, trim, or mutation of target memory.
        using (var target = DataTarget.AttachToProcess(pid, suspend: true))
        using (var process = Process.GetProcessById(pid))
        using (var runtime = target.ClrVersions.Single().CreateRuntime())
        {
            process.Refresh();
            long privateBytes = process.PrivateMemorySize64;
            var heap = runtime.Heap;
            if (!heap.CanWalkHeap) throw new InvalidOperationException("GC in progress; heap snapshot is not consistent. Retry in a fresh run.");
            var ranges = new List<OwnedRange>();
            var segments = heap.Segments.Select(s => new
            {
                Kind = s.Kind.ToString(), s.ObjectRange.Start, s.ObjectRange.End,
                CommittedStart = s.CommittedMemory.Start, CommittedEnd = s.CommittedMemory.End,
                ReservedStart = s.ReservedMemory.Start, ReservedEnd = s.ReservedMemory.End
            }).ToArray();
            foreach (var s in segments) ranges.Add(new(s.CommittedStart, s.CommittedEnd, "GC-active-" + s.Kind));
            var nativeHeaps = runtime.EnumerateClrNativeHeaps().Select(h => new
            { h.MemoryRange.Start, h.MemoryRange.End, Kind = h.Kind.ToString(), State = h.State.ToString() }).ToArray();
            foreach (var h in nativeHeaps) ranges.Add(new(h.Start, h.End, "CLR-native-" + h.Kind));
            foreach (var thread in runtime.Threads)
                ranges.Add(new(Math.Min(thread.StackBase, thread.StackLimit), Math.Max(thread.StackBase, thread.StackLimit), "Managed-thread-stack"));

            var regions = NativeMemoryMap.Read(process.Handle);
            var categories = Classify(regions, ranges);
            var types = new Dictionary<string, (long Count, ulong Bytes)>();
            long invalid = 0;
            foreach (var obj in heap.EnumerateObjects())
            {
                if (watch.Elapsed.TotalSeconds > 45) throw new TimeoutException("Bounded heap walk exceeded 45 seconds.");
                if (!obj.IsValid || obj.Type is null) { invalid++; continue; }
                string name = obj.Type.Name ?? "(unnamed)";
                var old = types.GetValueOrDefault(name);
                types[name] = (old.Count + 1, old.Bytes + obj.Size);
            }
            report = new
            {
                Version = 1, DiagnosticOnly = true, Pid = pid, TimestampUtc = DateTime.UtcNow,
                Runtime = target.ClrVersions.Single().Version.ToString(), ClrMd = typeof(DataTarget).Assembly.GetName().Version?.ToString(),
                PrivateBytes = privateBytes, WorkingSet = process.WorkingSet64, heap.IsServer, heap.CanWalkHeap, InvalidObjects = invalid,
                Categories = categories, Segments = segments, ClrNativeHeaps = nativeHeaps, Regions = regions,
                Types = types.OrderByDescending(p => p.Value.Bytes).Select(p => new { Type = p.Key, p.Value.Count, p.Value.Bytes }).ToArray(),
                PrivateCommit = regions.Where(r => r.State == 0x1000 && r.Type == 0x20000).Sum(r => (long)(r.End - r.Start)),
                CommittedAllocations = regions.Where(r => r.State == 0x1000).GroupBy(r => (r.AllocationBase, r.Type))
                    .Select(g => new { Base = g.Key.AllocationBase, g.Key.Type, Bytes = g.Sum(r => (long)(r.End - r.Start)) }).OrderByDescending(g => g.Bytes).ToArray(),
                Notes = "Paused address-space snapshot, no forced GC. Object table includes unreachable/uncollected objects; not a rooted-live-heap census. MEM_MAPPED/IMAGE may include private copy-on-write pages. CLR-native ranges are unioned with precedence after active segments; unknown private pages are not automatically a leak. Driver/kernel video memory is not all represented by process-private pages."
            };
        }
        string path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"[AddressSpace] {path}; {watch.Elapsed.TotalSeconds:F2}s; target resumed.");
    }

    internal static Dictionary<string, ulong> Classify(IEnumerable<Region> regions, List<OwnedRange> owners)
    {
        var totals = new Dictionary<string, ulong>();
        foreach (var region in regions.Where(r => r.State == 0x1000))
        {
            if (region.Type != 0x20000)
            {
                string key = region.Type == 0x40000 ? "Mapped-commit" : region.Type == 0x1000000 ? "Image-commit" : "Other-commit";
                totals[key] = totals.GetValueOrDefault(key) + region.End - region.Start;
                continue;
            }
            var overlap = owners.Where(o => o.Start < region.End && o.End > region.Start).ToArray();
            var cuts = overlap.SelectMany(o => new[] { Math.Max(o.Start, region.Start), Math.Min(o.End, region.End) })
                .Append(region.Start).Append(region.End).Distinct().Order().ToArray();
            for (int i = 0; i + 1 < cuts.Length; i++)
            {
                string kind = overlap.FirstOrDefault(o => o.Start <= cuts[i] && o.End >= cuts[i + 1]).Kind ?? "Private-unattributed";
                totals[kind] = totals.GetValueOrDefault(kind) + cuts[i + 1] - cuts[i];
            }
        }
        return totals;
    }
}

internal static class NativeMemoryMap
{
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct MemoryInfo
    {
        [FieldOffset(0)] public ulong Base;
        [FieldOffset(8)] public ulong AllocationBase;
        [FieldOffset(16)] public uint AllocationProtect;
        [FieldOffset(24)] public ulong Size;
        [FieldOffset(32)] public uint State;
        [FieldOffset(36)] public uint Protect;
        [FieldOffset(40)] public uint Type;
    }
    [DllImport("kernel32", SetLastError = true)]
    private static extern nuint VirtualQueryEx(IntPtr process, nuint address, out MemoryInfo info, nuint length);
    [DllImport("kernel32", SetLastError = true)]
    internal static extern IntPtr VirtualAlloc(IntPtr address, nuint size, uint type, uint protect);
    [DllImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFree(IntPtr address, nuint size, uint type);

    internal static List<Region> Read(IntPtr process)
    {
        var result = new List<Region>();
        ulong address = 0;
        while (true)
        {
            nuint size = VirtualQueryEx(process, (nuint)address, out var r, 48);
            if (size == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 87) throw new System.ComponentModel.Win32Exception(error);
                break;
            }
            if (size != 48 || r.Size == 0 || r.Base + r.Size <= address) throw new InvalidDataException("Invalid address-space region.");
            if (r.State != 0x10000) result.Add(new(r.Base, r.Base + r.Size, r.AllocationBase, r.State, r.Type, r.Protect));
            address = r.Base + r.Size;
        }
        return result;
    }
}

internal static class ProbeTests
{
    internal static void Run()
    {
        var categories = MemoryCapture.Classify([new(0, 100, 0, 0x1000, 0x20000, 4)],
            [new(10, 50, "active"), new(20, 80, "native"), new(30, 40, "duplicate")]);
        if (categories["active"] != 40 || categories["native"] != 30 || categories["Private-unattributed"] != 30 || categories.ContainsKey("duplicate"))
            throw new InvalidDataException("Overlapping owner ranges were double-counted.");
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "self-test-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(ProbeTests).Assembly.Location);
        start.ArgumentList.Add("fixture");
        using var child = Process.Start(start)!;
        try
        {
            if (child.StandardOutput.ReadLine() != "ready") throw new InvalidDataException("Fixture not ready.");
            MemoryCapture.Write(child.Id, Path.Combine(folder, "before.json"));
            child.StandardInput.WriteLine("allocate"); child.StandardInput.Flush();
            ulong address = ulong.Parse(child.StandardOutput.ReadLine()!);
            MemoryCapture.Write(child.Id, Path.Combine(folder, "allocated.json"));
            using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "allocated.json")));
            if (!data.RootElement.GetProperty("CommittedAllocations").EnumerateArray().Any(r => r.GetProperty("Base").GetUInt64() == address && r.GetProperty("Bytes").GetInt64() == 24 * 1024 * 1024))
                throw new InvalidDataException("Known native allocation missing.");
            if (!data.RootElement.GetProperty("Types").EnumerateArray().Any(t => t.GetProperty("Type").GetString() == "System.Byte[]" && t.GetProperty("Bytes").GetUInt64() >= 64 * 1024 * 1024))
                throw new InvalidDataException("Known managed allocation missing.");
            child.StandardInput.WriteLine("free"); child.StandardInput.Flush();
            if (child.StandardOutput.ReadLine() != "freed") throw new InvalidDataException("Fixture did not resume/release.");
            MemoryCapture.Write(child.Id, Path.Combine(folder, "released.json"));
            using var released = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "released.json")));
            if (released.RootElement.GetProperty("CommittedAllocations").EnumerateArray().Any(r => r.GetProperty("Base").GetUInt64() == address && r.GetProperty("Bytes").GetInt64() >= 24 * 1024 * 1024))
                throw new InvalidDataException("Native allocation did not release.");
            Console.WriteLine("PASS: range precedence, known managed/native allocations, resume and release. " + folder);
        }
        finally
        {
            if (!child.HasExited) { child.StandardInput.Close(); if (!child.WaitForExit(5000)) child.Kill(); }
            child.WaitForExit();
        }
    }

    internal static void Fixture()
    {
        Console.WriteLine("ready");
        byte[]? bytes = null;
        IntPtr native = IntPtr.Zero;
        try
        {
            string? command;
            while ((command = Console.ReadLine()) != null)
            {
                if (command == "allocate")
                {
                    bytes = new byte[64 * 1024 * 1024]; Array.Fill(bytes, (byte)1);
                    native = NativeMemoryMap.VirtualAlloc(IntPtr.Zero, 24 * 1024 * 1024, 0x3000, 4);
                    if (native == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    Console.WriteLine((ulong)native);
                }
                else if (command == "free")
                {
                    bytes = null;
                    if (!NativeMemoryMap.VirtualFree(native, 0, 0x8000)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    native = IntPtr.Zero;
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    Console.WriteLine("freed");
                }
                GC.KeepAlive(bytes);
            }
        }
        finally { if (native != IntPtr.Zero) NativeMemoryMap.VirtualFree(native, 0, 0x8000); }
    }
}
