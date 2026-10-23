using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Vortice.Direct3D11;

internal static class PbrPerformanceChecks
{
    public static unsafe void Run(string root, bool reverse = false, bool reflections = false, bool rooms = false)
    {
        var startup = Stopwatch.StartNew();
        using var host = new EngineHost(1920, 1080, "Material GPU profile");
        using var game = new Game.HL2GameModule(Path.Combine(root, "Game/Content/Levels", rooms ? RoomReflectionChecks.FileName : PbrTestRoom.FileName));
        game.AssetStreamingEnabled = false; game.Initialize(host.Context);
        var renderer = host.Context.Renderer;
        var device = renderer.GraphicsDevice;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        var cache = (IDictionary)PbrBaselineChecks.Field(game, "_weaponModelCache");
        var world = (BasicWorldRenderer)PbrBaselineChecks.Field(game, "_world");
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game, "_camera");
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game, "_editor");
        editor.LevelFile.ReflectionEnvironment = reflections && !rooms ? ReflectionPreset.SyntheticStudio : ReflectionPreset.None;
        var roomSession = (ReflectionProbeSession)PbrBaselineChecks.Field(game, "_roomReflections");
        world.ViewCullingEnabled = false;
        do
        {
            target.Frame(() => game.RenderWorld(renderer));
            if ((!reflections || world.ReflectionReady) && cache.Count > 0 && cache.Values.Cast<object>().All(e => PbrBaselineChecks.Field(e, "Model") != null)) break;
            if (startup.Elapsed.TotalSeconds > 30) throw new TimeoutException("Material fixture assets not ready.");
            Thread.Sleep(5);
        } while (true);
        world.ViewCullingEnabled = true;
        Console.WriteLine($"[PbrReady] {startup.Elapsed.TotalMilliseconds:F2}ms host/game construction to all requested model GPU readback, warm filesystem; not first-playable/cold start.");
        Marshal.AddRef(device.GetD3D11Info().Device);
        using var native = new ID3D11Device(device.GetD3D11Info().Device);
        using var context = native.ImmediateContext;
        using var disjoint = native.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
        using var beginQuery = native.CreateQuery(new QueryDescription(QueryType.Timestamp));
        using var endQuery = native.CreateQuery(new QueryDescription(QueryType.Timestamp));
        var modeProperty = typeof(LevelFile).GetProperty("LightingModel");
        Console.WriteLine($"[PbrGPU] {target.Width}x{target.Height}, RGBA16F/4xMSAA, D3D11 debug layer ON, exposure {editor.LevelFile.Exposure}, ambient {editor.LevelFile.AmbientLight}, directional {editor.LevelFile.DirectionalLight}; shadowed point 6/range10, spot 7/range9, torch 7/range22. 30 warmup + 180 measured; no Update/physics/UI/present, synchronous timestamp waits test-only.");
        world.MeasureShadowCpuTime = true;
        var results = new Dictionary<(int Mode, bool Torch), (double Cpu, double Cpu95, double Gpu, double Gpu95)>();
        foreach (int mode in modeProperty == null ? new[] { 0 } : reverse ? new[] { 1, 0, 1, 0 } : new[] { 0, 1, 0, 1 })
        foreach (bool torch in new[] { false, true })
        {
            if (modeProperty != null) modeProperty.SetValue(editor.LevelFile, Enum.ToObject(modeProperty.PropertyType, reflections ? 1 : mode));
            editor.LevelFile.ReflectionEnvironment = reflections && !rooms && mode==1 ? ReflectionPreset.SyntheticStudio : ReflectionPreset.None;
            if (rooms) roomSession.PreviewEnabled = mode == 1;
            PbrBaselineChecks.Set(game, "_flashlightOn", torch);
            var cpu = new List<double>(); var gpu = new List<double>();
            double allocations = 0, shadowCpu = 0; long draws = 0, uploads = 0, peakManaged = 0, peakWorking = 0;
            int[] gcStart = new int[3];
            for (int frame = -30; frame < 180; frame++)
            {
                if (frame == 0) for (int generation = 0; generation < 3; generation++) gcStart[generation] = GC.CollectionCount(generation);
                float t = Math.Max(0, frame) / 60f;
                camera.Position = new(MathF.Sin(t * 1.1f) * .7f, 1.65f, -2.7f + .25f * MathF.Sin(t));
                camera.Yaw = MathF.Sin(t) * .1f; camera.Pitch = .04f;
                long allocated = GC.GetAllocatedBytesForCurrentThread(), stamp = Stopwatch.GetTimestamp();
                renderer.BeginFrame(); game.RenderWorld(renderer); renderer.ResolveWorldToSwapchain(target.Framebuffer); renderer.CommandList.End();
                double ms = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                context.Begin(disjoint); context.End(beginQuery); device.SubmitCommands(renderer.CommandList);
                context.End(endQuery); context.End(disjoint); device.WaitForIdle(); context.Flush();
                QueryDataTimestampDisjoint clock = default; ulong begin = 0, end = 0;
                var wait = Stopwatch.StartNew();
                while (context.GetData(disjoint, (IntPtr)(&clock), sizeof(QueryDataTimestampDisjoint), AsyncGetDataFlags.DoNotFlush).Code != 0 ||
                    context.GetData(beginQuery, (IntPtr)(&begin), sizeof(ulong), AsyncGetDataFlags.DoNotFlush).Code != 0 ||
                    context.GetData(endQuery, (IntPtr)(&end), sizeof(ulong), AsyncGetDataFlags.DoNotFlush).Code != 0)
                { if (wait.Elapsed.TotalSeconds > 5) throw new TimeoutException("GPU timestamp unavailable."); Thread.Sleep(1); }
                if (frame < 0) continue;
                if (clock.Disjoint || clock.Frequency == 0 || end < begin) throw new InvalidDataException("Invalid GPU timestamp sample.");
                cpu.Add(ms); gpu.Add((end - begin) * 1000.0 / clock.Frequency);
                allocations += bytes; shadowCpu += world.ShadowCpuMilliseconds; draws += world.ObjectDrawCount; uploads += world.ObjectUploadCount;
                peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                using var process = Process.GetCurrentProcess(); peakWorking = Math.Max(peakWorking, process.WorkingSet64);
            }
            cpu.Sort(); gpu.Sort();
            results[(mode,torch)] = (cpu.Average(),cpu[170],gpu.Average(),gpu[170]);
            if (uploads != 180 || game.StreamingResidentBytes > 48.19*1048576*1.05)
                throw new InvalidDataException("PBR upload/memory baseline budget exceeded.");
            long reflectionBytes = world.ReflectionResidentBytes + (rooms && world.Reflection == null ? roomSession.ResidentBytes : 0);
            if (reflectionBytes > 1.1*1048576 || world.ReflectionPendingCpuBytes != 0)
                throw new InvalidDataException("Reflection memory budget exceeded or upload incomplete.");
            if (reflections) Console.WriteLine($"[Reflection] enabled={mode==1}; GPU payload {reflectionBytes} bytes including fallback/metadata and retained preview; pending CPU {world.ReflectionPendingCpuBytes} bytes.");
            Console.WriteLine($"[PbrGPU] mode={mode} torch={torch}: CPU mean/P95 {cpu.Average():F3}/{cpu[170]:F3}ms, shadow CPU {shadowCpu/180:F3}ms, GPU command mean/P95 {gpu.Average():F3}/{gpu[170]:F3}ms; {allocations/180/1024:F2}KiB allocations/frame; GC {string.Join('/', Enumerable.Range(0,3).Select(g=>GC.CollectionCount(g)-gcStart[g]))}; draws {draws/180.0:F1}, uploads {uploads/180.0:F1}; resident models {game.StreamingResidentBytes/1048576.0:F2}MiB, peak managed/working {peakManaged/1048576.0:F1}/{peakWorking/1048576.0:F1}MiB.");
        }
        world.MeasureShadowCpuTime = false;
        if (modeProperty != null) foreach (bool torch in new[]{false,true})
        {
            var legacy = results[(0,torch)]; var pbr = results[(1,torch)];
            Console.WriteLine($"[PbrBudget] steady repeated pair torch={torch}: CPU delta {pbr.Cpu-legacy.Cpu:F3}/{pbr.Cpu95-legacy.Cpu95:F3}ms; GPU delta {pbr.Gpu-legacy.Gpu:F3}/{pbr.Gpu95-legacy.Gpu95:F3}ms (mean/P95).");
            if (pbr.Cpu > legacy.Cpu+.5 || pbr.Cpu95 > legacy.Cpu95+1 || pbr.Gpu > legacy.Gpu+1 || pbr.Gpu95 > legacy.Gpu95+1)
                throw new InvalidDataException("PBR increment exceeds the recorded CPU/GPU budget. Check both orders and system load; do not widen the budget.");
        }
    }
}
