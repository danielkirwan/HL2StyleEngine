using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Engine.Editor.Editor;
using Engine.Render;
using Engine.Runtime.Hosting;
using Game;
using Veldrid;
using Vortice.Direct3D11;

internal static class FlashlightPerformanceChecks
{
    internal static unsafe void Run(HL2GameModule game, EngineHost host, Framebuffer target)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        T Field<T>(string name) => (T)typeof(HL2GameModule).GetField(name, flags)!.GetValue(game)!;
        var flashlight = typeof(HL2GameModule).GetField("_flashlightOn", flags)!;
        var camera = Field<FpsCamera>("_camera");
        var editor = Field<LevelEditorController>("_editor");
        var world = Field<BasicWorldRenderer>("_world");
        var renderer = host.Context.Renderer;
        var device = renderer.GraphicsDevice;
        Marshal.AddRef(device.GetD3D11Info().Device);
        using var native = new ID3D11Device(device.GetD3D11Info().Device);
        using var context = native.ImmediateContext;
        using var disjoint = native.CreateQuery(new QueryDescription(QueryType.TimestampDisjoint));
        using var beginQuery = native.CreateQuery(new QueryDescription(QueryType.Timestamp));
        using var endQuery = native.CreateQuery(new QueryDescription(QueryType.Timestamp));
        world.MeasureShadowCpuTime = true;
        Console.WriteLine($"[FlashlightProfile] {target.Width}x{target.Height}; warmed assets; 30 warmup + 180 measured frames/mode; three rounds; deterministic camera route; no gameplay update/physics/UI/present; synchronous GPU readback is test-only.");
        Console.WriteLine("Round,Route,Mode,CpuRenderMeanMs,CpuRenderP95Ms,ShadowCpuMeanMs,GpuCommandsMeanMs,GpuCommandsP95Ms,ShadowDrawsMean,TransformUploadsMean,AllocKiBPerFrame");
        for (int round = 1; round <= 3; round++)
        foreach (bool moving in new[] { false, true })
        foreach (int mode in round % 2 == 1 ? new[] { 0, 1, 2, 3 } : new[] { 3, 2, 1, 0 })
        {
            flashlight.SetValue(game, mode != 0);
            editor.LevelFile.EnableShadows = mode >= 2;
            world.ShadowBatchUploadsEnabled = mode != 2;
            var cpu = new List<double>(); var gpu = new List<double>();
            double shadow = 0, draws = 0, uploads = 0, allocations = 0;
            for (int frame = -30; frame < 180; frame++)
            {
                float t = Math.Max(0, frame) / 60f;
                camera.Position = moving ? new Vector3(MathF.Sin(t * 1.3f) * 2, 1.65f, -10 + t * 1.5f) : new(0, 1.65f, -10);
                camera.Yaw = moving ? MathF.Sin(t * 1.1f) * .65f : 0;
                camera.Pitch = moving ? MathF.Sin(t) * .12f - .08f : -.08f;
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long stamp = Stopwatch.GetTimestamp();
                renderer.BeginFrame();
                game.RenderWorld(renderer);
                renderer.ResolveWorldToSwapchain(target);
                renderer.CommandList.End();
                double recordMs = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                context.Begin(disjoint); context.End(beginQuery);
                device.SubmitCommands(renderer.CommandList);
                context.End(endQuery); context.End(disjoint);
                device.WaitForIdle(); context.Flush();
                QueryDataTimestampDisjoint clock = default;
                ulong begin = 0, end = 0;
                var timeout = Stopwatch.StartNew();
                while (context.GetData(disjoint, (IntPtr)(&clock), sizeof(QueryDataTimestampDisjoint), AsyncGetDataFlags.DoNotFlush).Code != 0 ||
                    context.GetData(beginQuery, (IntPtr)(&begin), sizeof(ulong), AsyncGetDataFlags.DoNotFlush).Code != 0 ||
                    context.GetData(endQuery, (IntPtr)(&end), sizeof(ulong), AsyncGetDataFlags.DoNotFlush).Code != 0)
                {
                    if (timeout.Elapsed.TotalSeconds > 5) throw new TimeoutException("GPU profile readback timed out.");
                    Thread.Sleep(1);
                }
                if (frame < 0) continue;
                cpu.Add(recordMs); shadow += world.ShadowCpuMilliseconds; draws += world.ShadowDrawCount;
                uploads += world.ShadowObjectUploadCount; allocations += allocated;
                if (!clock.Disjoint && clock.Frequency > 0 && end >= begin) gpu.Add((end - begin) * 1000.0 / clock.Frequency);
            }
            if (gpu.Count != 180) throw new InvalidDataException("Incomplete/disjoint GPU timestamp samples.");
            cpu.Sort(); gpu.Sort();
            Console.WriteLine($"{round},{(moving ? "moving" : "stationary")},{new[] { "off", "on-no-shadows", "on-shadowed-reference", "on-shadowed-batched" }[mode]}," +
                $"{cpu.Average():F3},{cpu[(int)((cpu.Count - 1) * .95)]:F3},{shadow / cpu.Count:F3}," +
                $"{gpu.Average():F3},{gpu[(int)((gpu.Count - 1) * .95)]:F3},{draws / cpu.Count:F1},{uploads / cpu.Count:F1},{allocations / cpu.Count / 1024:F1}");
        }
        world.MeasureShadowCpuTime = false;
        world.ShadowBatchUploadsEnabled = true;
    }
}
