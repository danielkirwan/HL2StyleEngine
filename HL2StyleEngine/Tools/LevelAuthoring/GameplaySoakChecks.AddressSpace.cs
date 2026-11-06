using System.Diagnostics;
using System.Runtime.InteropServices;
using Engine.Runtime.Hosting;
using Veldrid;
using Vortice.Direct3D11;
using Vortice.DXGI;

internal sealed partial class GameplaySoakChecks
{
    private bool _addressSnapshots;
    private string _addressProbe = "";
    private readonly List<object> _addressCheckpoints = new();

    private void CaptureAddressSpace(string label)
    {
        if (!File.Exists(_addressProbe)) throw new FileNotFoundException("Build Tools/MemoryProbe Release before address-space tests.", _addressProbe);
        object gpu = ReadGpuMemory();
        using var self = Process.GetCurrentProcess();
        self.Refresh();
        var gc = GC.GetGCMemoryInfo();
        long beforePrivate = self.PrivateMemorySize64;
        string path = Path.Combine(_folder, "address-" + label + ".json");
        var start = new ProcessStartInfo(_addressProbe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] { "capture", Environment.ProcessId.ToString(), path }) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_diagnosticTool)!, ".."));
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Address-space collector did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string log = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
        File.WriteAllText(Path.ChangeExtension(path, ".log"), log);
        if (process.ExitCode != 0) throw new InvalidDataException("Address-space capture failed: " + path);
        self.Refresh();
        _addressCheckpoints.Add(new
        {
            Label = label, Frame = _frame, Cycle = _completedCycles, Path = path,
            BeforePrivate = beforePrivate, AfterPrivate = self.PrivateMemorySize64, Seconds = watch.Elapsed.TotalSeconds,
            gc.Index, gc.TotalCommittedBytes, gc.HeapSizeBytes, gc.FragmentedBytes, Gpu = gpu,
            Note = "External suspended read; no GC during cycle captures. Post-run-collected follows existing post-timing diagnostic GC and frame-report serialization. Not performance acceptance."
        });
        Console.WriteLine($"[AddressSpace] {label}, {watch.Elapsed.TotalSeconds:F2}s, no target collection during capture.");
    }

    private object ReadGpuMemory()
    {
        try
        {
            var device = ((EngineContext)Field("_ctx")).Renderer.GraphicsDevice;
            if (device.BackendType != GraphicsBackend.Direct3D11) return new { Available = false, Reason = "Not D3D11" };
            IntPtr pointer = device.GetD3D11Info().Device;
            Marshal.AddRef(pointer);
            using var native = new ID3D11Device(pointer);
            using var dxgi = native.QueryInterface<IDXGIDevice>();
            dxgi.GetAdapter(out var adapter).CheckError();
            using (adapter)
            using (var budget = adapter.QueryInterface<IDXGIAdapter3>())
            {
                var local = budget.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
                var shared = budget.QueryVideoMemoryInfo(0, MemorySegmentGroup.NonLocal);
                return new
                {
                    Available = true, LocalUsage = local.CurrentUsage, LocalReservation = local.CurrentReservation, LocalBudget = local.Budget,
                    NonLocalUsage = shared.CurrentUsage, NonLocalReservation = shared.CurrentReservation, NonLocalBudget = shared.Budget
                };
            }
        }
        catch (Exception ex) { return new { Available = false, Reason = ex.Message }; }
    }
}
