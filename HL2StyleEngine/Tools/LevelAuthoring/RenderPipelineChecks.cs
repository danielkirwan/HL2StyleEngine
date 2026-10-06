using System.Numerics;
using System.Drawing;
using System.Drawing.Imaging;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static class RenderPipelineChecks
{
    public static void Run()
    {
        using var host = new EngineHost(640, 480, "Rendering QA");
        var renderer = host.Context.Renderer;
        var device = renderer.GraphicsDevice;
        System.Runtime.InteropServices.Marshal.AddRef(device.GetD3D11Info().Device);
        using var nativeDevice = new Vortice.Direct3D11.ID3D11Device(device.GetD3D11Info().Device);
        using var messages = nativeDevice.QueryInterface<Vortice.Direct3D11.Debug.ID3D11InfoQueue>();
        using var world = new BasicWorldRenderer(device, renderer.WorldOutputDescription, "Shaders");
        world.AmbientStrength = .015f; world.DirectionalStrength = 0;
        var source = new LoadedModel([new LoadedModelPart(
            [new(-5, -2, -4), new(5, -2, -4), new(5, 4, -4), new(-5, 4, -4)],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0,0), new(1,0), new(1,1), new(0,1)], [0,1,2,0,2,3], Vector4.One, null, null, 0, 1)
        { BaseColorTexture = new PreparedTexture("qa-white", 1, 1, TextureSemantic.Color, false, [[255,255,255,255]]) }]);
        using var model = world.CreateRenderModel(source);
        using var second = world.CreateRenderModel(source);
        Require(world.ResidentTextureCount == 3 && world.TextureResidentBytes == 12, "Shared texture resources were duplicated.");
        Require(world.TryCreateRenderModel(new LoadedModel([]), out var empty) && empty!.IsReady, "Empty model upload did not complete.");
        empty!.Dispose();
        double originalBudget = world.UploadBudgetMilliseconds;
        world.UploadBudgetMilliseconds = double.Epsilon;
        var stagedSource = new LoadedModel([source.Parts[0], source.Parts[0]]);
        world.BeginFrame();
        Require(!world.TryCreateRenderModel(stagedSource, out var staged) && staged == null, "Partial model escaped the upload budget.");
        world.BeginFrame();
        Require(world.TryCreateRenderModel(stagedSource, out staged) && staged!.IsReady, "Budgeted upload did not resume next frame.");
        staged!.Dispose();
        Require(world.ResidentTextureCount == 3, "Disposing shared model textures removed live resources.");
        world.UploadBudgetMilliseconds = originalBudget;
        world.BeginFrame();
        using (world.BatchObjectUploads()) { }
        Require(world.ObjectUploadCount == 0, "Empty object batches must not upload stale data.");
        if (OperatingSystem.IsWindows())
        {
            using var bitmap = new Bitmap(13, 7);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var oddTexture = TextureCooker.PrepareImage(stream.ToArray(), TextureSemantic.Color, null, cook: true);
            var originalTexture = source.Parts[0].BaseColorTexture;
            source.Parts[0].BaseColorTexture = oddTexture;
            using var oddModel = world.CreateRenderModel(source);
            source.Parts[0].BaseColorTexture = originalTexture;
            Require(oddModel.IsReady, "Non-block-aligned cooked texture did not upload.");
        }
        int width = (int)renderer.WorldFramebuffer.Width, height = (int)renderer.WorldFramebuffer.Height;
        using var staging = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1,
            device.MainSwapchain.Framebuffer.ColorTargets[0].Target.Format, TextureUsage.Staging));
        using var captureColor = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, staging.Format, TextureUsage.RenderTarget));
        using var captureDepth = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)width, (uint)height, 1, 1, Veldrid.PixelFormat.R32_Float, TextureUsage.DepthStencil));
        using var captureFrame = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(captureDepth, captureColor));
        var eye = new Vector3(3, 2, 5);
        var viewProj = Matrix4x4.CreateLookAt(eye, new Vector3(0, .5f, -3), Vector3.UnitY) *
            Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, width / (float)height, .05f, 100);
        var cube = Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateTranslation(0, 0, -1.5f);
        var light = new WorldPointLight(new Vector3(-2, 2, 1), Vector3.One, 7, 15,
            Vector3.Normalize(new Vector3(2, -2, -5)), 85, false);
        try
        {
            uint[] unshadowed = Frame(light, cube);
            Require(world.ObjectUploadCount == 1, "Main scene objects were not uploaded in one batch.");
            world.ObjectBatchUploadsEnabled = false;
            Require(Frame(light, cube).SequenceEqual(unshadowed) && world.ObjectUploadCount == 2,
                "Batched main-scene objects differ from individual uploads.");
            world.ObjectBatchUploadsEnabled = true;
            world.ViewCullingEnabled = false;
            Require(Frame(light, cube, extraBoxes: 4100).SequenceEqual(unshadowed) && world.ObjectUploadCount == 1,
                "Object ring overflow changed the image or submitted excess uploads.");
            world.ObjectBatchUploadsEnabled = false;
            Require(Frame(light, cube, extraBoxes: 4100).SequenceEqual(unshadowed) && world.ObjectUploadCount == 4096,
                "Batched and reference object ring limits disagree.");
            world.ObjectBatchUploadsEnabled = true;
            world.ViewCullingEnabled = true;
            uint[] shadowed = Frame(light with { CastShadows = true }, cube);
            Require(world.ShadowObjectUploadCount == 1, "Shadow matrices were not uploaded in one batch.");
            world.ShadowBatchUploadsEnabled = false;
            world.ShadowCachingEnabled = false;
            Require(Frame(light with { CastShadows = true }, cube).SequenceEqual(shadowed), "Batched shadow objects differ from individual uploads.");
            world.ShadowBatchUploadsEnabled = true;
            world.ShadowCachingEnabled = true;
            int darker = CountDarker(unshadowed, shadowed);
            Require(world.ShadowPassCount == 1 && darker > 300, $"Spot shadow missing: {darker} darker pixels.");
            uint[] cached = Frame(light with { CastShadows = true }, cube);
            Require(world.ShadowCacheHits == 1 && world.ShadowRenderedPassCount == 0 && world.ShadowDrawCount == 0 && cached.SequenceEqual(shadowed), "Cached spotlight differs from fresh rendering.");
            var dimmed = light with { CastShadows = true, Intensity = 2 };
            cached = Frame(dimmed, cube);
            Require(world.ShadowCacheHits == 1 && CountDarker(shadowed, cached) > 1000, "Intensity/flicker change should reuse depth but update lighting.");
            world.ShadowCachingEnabled = false;
            Require(Frame(dimmed, cube).SequenceEqual(cached), "Intensity update used stale shadow data.");
            world.ShadowCachingEnabled = true;
            Frame(light with { CastShadows = true }, cube, overflow: true);
            Require(world.ShadowCastersOmitted > 0, "Excess shadow casters were not bounded.");
            uint[] moved = Frame(light with { CastShadows = true }, Matrix4x4.CreateScale(1.5f) * Matrix4x4.CreateTranslation(2, 0, -1.5f));
            Require(CountDarker(shadowed, moved) + CountDarker(moved, shadowed) > 1000, "Moving occluder did not update the shadow.");
            uint[] pointUnshadowed = Frame(light with { SpotAngleDegrees = 0 }, cube);
            uint[] point = Frame(light with { SpotAngleDegrees = 0, CastShadows = true }, cube);
            Require(world.ShadowPassCount == 6 && CountDarker(pointUnshadowed, point) > 300, "Point-light cube shadow missing.");
            Require(Frame(light with { SpotAngleDegrees = 0, CastShadows = true }, cube).SequenceEqual(point) &&
                world.ShadowCacheHits == 6 && world.ShadowDrawCount == 0, "Point-light cube cache failed.");
            uint[] disabled = Frame(light with { Intensity = 0, CastShadows = true }, cube);
            Require(CountDarker(unshadowed, disabled) > 10000 && world.ShadowPassCount == 0, "Disabled light still contributes.");
            Require(Frame(light with { SpotAngleDegrees = 0, CastShadows = true }, cube).SequenceEqual(point) && world.ShadowRenderedPassCount == 6,
                "Re-enabled light did not rebuild cleared cache slots.");
            renderer.Exposure = 2;
            uint[] exposed = Frame(light, cube);
            Require(CountDarker(exposed, unshadowed) > 10000, "Exposure control did not reach the presentation pass.");
            renderer.Exposure = 1;
            CheckShadowInvalidation();
            BenchmarkShadows();
            string output = Path.Combine(AppContext.BaseDirectory, "qa");
            Directory.CreateDirectory(output);
            SavePng(shadowed, width, height, Path.Combine(output, "spot-shadow.png"));
            SavePng(point, width, height, Path.Combine(output, "point-shadow.png"));
            Console.WriteLine($"PASS: real GPU shared textures, HDR/exposure, spot/point shadows, cache invalidation, frustum culling and 24 moving-door/light frames matching the uncached reference. Spot shadow affected {darker} pixels.");
            Console.WriteLine($"Render captures: {output}");
        }
        finally { device.WaitForIdle(); }

        uint[] Frame(WorldPointLight testLight, Matrix4x4 transform, bool overflow = false,
            IReadOnlyList<ShadowCaster>? customCasters = null, WorldPointLight[]? customLights = null, int extraBoxes = 0)
        {
            renderer.BeginFrame(); world.BeginFrame(); world.UpdateCamera(viewProj, eye);
            world.UpdatePointLights(customLights ?? [testLight], eye);
            var caster = new ShadowCaster(null, transform, new Vector3(transform.M41, transform.M42, transform.M43), 1.3f);
            ShadowCaster[] casters = overflow
                ? Enumerable.Repeat(caster with { Center = new Vector3(1000), Transform = Matrix4x4.CreateTranslation(1000, 0, 0) }, 2100).Prepend(caster).ToArray()
                : [caster];
            world.RenderShadows(renderer, customCasters ?? casters,
                new Viewport(0, 0, width, height, 0, 1));
            using (world.BatchObjectUploads())
            {
                world.DrawModel(renderer.CommandList, model, Matrix4x4.Identity, Vector4.One);
                world.DrawBox(renderer.CommandList, transform, new Vector4(.5f, .2f, .08f, 1));
                for (int i = 0; i < extraBoxes; i++)
                    world.DrawBox(renderer.CommandList, Matrix4x4.CreateTranslation(1000, 0, 0), Vector4.One);
            }
            renderer.ResolveWorldToSwapchain(captureFrame);
            try { renderer.CommandList.CopyTexture(captureColor, staging); }
            catch
            {
                for (ulong i = 0; i < messages.NumStoredMessages; i++) Console.Error.WriteLine(messages.GetMessage(i).Description);
                throw;
            }
            renderer.EndFrame(); device.WaitForIdle();
            var mapped = device.Map<uint>(staging, MapMode.Read);
            try
            {
                var pixels = new uint[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = mapped[x, y];
                return pixels;
            }
            finally { device.Unmap(staging); }
        }

        void CheckShadowInvalidation()
        {
            var spot = light with { CastShadows = true };
            var caster = new ShadowCaster(null, cube, new(0, 0, -1.5f), 1.3f);
            var behind = caster with { Center = light.Position - light.Direction * 5, Radius = .5f,
                Transform = Matrix4x4.CreateScale(.3f) * Matrix4x4.CreateTranslation(light.Position - light.Direction * 5) };
            world.ShadowCachingEnabled = false; world.ShadowCullingEnabled = false;
            uint[] reference = Frame(spot, cube, customCasters: [caster, behind]);
            int beforeDraws = world.ShadowDrawCount;
            world.ShadowCullingEnabled = true;
            Require(Frame(spot, cube, customCasters: [caster, behind]).SequenceEqual(reference) &&
                world.ShadowDrawCount < beforeDraws && world.ShadowCulledCasters > 0, "Frustum culling changed image or did not reduce draws.");
            world.ShadowCachingEnabled = true;
            Frame(spot, cube, customCasters: [behind, caster]);
            Require(world.ShadowCacheHits == 1, "Invisible object ordering invalidated cached depth.");
            var removed = Frame(spot, cube, customCasters: []);
            Require(world.ShadowRenderedPassCount == 1, "Removing the last caster kept its old shadow.");
            world.ShadowCachingEnabled = false;
            Require(Frame(spot, cube, customCasters: []).SequenceEqual(removed), "Removed caster left a stale shadow.");
            world.ShadowCachingEnabled = true;

            var hidden = new HashSet<string>();
            var meshCaster = new ShadowCaster(model, Matrix4x4.CreateTranslation(0, 0, 3), new(0, 1, -1), 6, hidden);
            Frame(spot, cube, customCasters: [meshCaster]);
            hidden.Add(source.Parts[0].PartKey);
            var broken = Frame(spot, cube, customCasters: [meshCaster]);
            Require(world.ShadowRenderedPassCount == 1 && world.ShadowDrawCount == 0, "In-place crate hidden-part change did not invalidate shadow.");
            world.ShadowCachingEnabled = false;
            Require(Frame(spot, cube, customCasters: [meshCaster]).SequenceEqual(broken), "Broken mesh shadow differs from reference.");
            world.ShadowCachingEnabled = true;
            hidden.Clear();
            Frame(spot, cube, customCasters: [meshCaster]);
            Frame(spot, cube, customCasters: [meshCaster with { Model = second }]);
            Require(world.ShadowRenderedPassCount == 1, "Model replacement reused previous model depth.");

            for (int step = 0; step < 24; step++)
            {
                float angle = step * MathF.PI / 24;
                var door = Matrix4x4.CreateTranslation(.5f, 0, 0) * Matrix4x4.CreateRotationY(angle) * Matrix4x4.CreateTranslation(-.5f, 0, -1.5f);
                var movingLight = spot with { Position = light.Position + new Vector3(MathF.Sin(angle), 0, 0), SpotAngleDegrees = 70 + step };
                var other = spot with { Position = new(2, 3, 2), Direction = Vector3.Normalize(new Vector3(-2, -2, -6)) };
                WorldPointLight[] lights = step % 2 == 0 ? [movingLight, other] : [other, movingLight];
                var optimized = Frame(movingLight, door, customLights: lights);
                world.ObjectBatchUploadsEnabled = false;
                world.ViewCullingEnabled = false;
                world.ShadowBatchUploadsEnabled = false;
                world.ShadowCachingEnabled = false; world.ShadowCullingEnabled = false;
                Require(Frame(movingLight, door, customLights: lights).SequenceEqual(optimized), $"Door/light stress frame {step} differs from uncached/unculled path.");
                world.ShadowBatchUploadsEnabled = true;
                world.ObjectBatchUploadsEnabled = true;
                world.ViewCullingEnabled = true;
                world.ShadowCachingEnabled = true; world.ShadowCullingEnabled = true;
            }
        }

        unsafe void BenchmarkShadows()
        {
            using var context = nativeDevice.ImmediateContext;
            using var disjoint = nativeDevice.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.TimestampDisjoint));
            using var start = nativeDevice.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Timestamp));
            using var end = nativeDevice.CreateQuery(new Vortice.Direct3D11.QueryDescription(Vortice.Direct3D11.QueryType.Timestamp));
            var casters = Enumerable.Range(0, 600).Select(i =>
            {
                var center = new Vector3(i % 20 * .35f - 3.5f, (i / 20) % 5 * .4f, i / 100 * -.5f - 1);
                return new ShadowCaster(null, Matrix4x4.CreateScale(.25f) * Matrix4x4.CreateTranslation(center), center, .22f);
            }).ToArray();
            var point = light with { CastShadows = true, SpotAngleDegrees = 0 };
            foreach (var mode in new[] { "uncached/unculled", "uncached/culled", "cached/culled" })
            {
                world.ShadowCachingEnabled = mode == "cached/culled";
                world.ShadowCullingEnabled = mode != "uncached/unculled";
                var cpu = new List<double>(); var gpu = new List<double>();
                int draws = 0;
                for (int i = 0; i < 70; i++)
                {
                    renderer.BeginFrame(); world.BeginFrame(); world.UpdateCamera(viewProj, eye); world.UpdatePointLights([point], eye);
                    long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
                    world.RenderShadows(renderer, casters, new Viewport(0, 0, width, height, 0, 1));
                    double recordMs = System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
                    renderer.CommandList.End();
                    context.Begin(disjoint); context.End(start);
                    device.SubmitCommands(renderer.CommandList);
                    context.End(end); context.End(disjoint);
                    device.WaitForIdle();
                    Vortice.Direct3D11.QueryDataTimestampDisjoint clock = default;
                    ulong begin = 0, finish = 0;
                    context.Flush();
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (context.GetData(disjoint, (IntPtr)(&clock), sizeof(Vortice.Direct3D11.QueryDataTimestampDisjoint), Vortice.Direct3D11.AsyncGetDataFlags.DoNotFlush).Code != 0 ||
                        context.GetData(start, (IntPtr)(&begin), sizeof(ulong), Vortice.Direct3D11.AsyncGetDataFlags.DoNotFlush).Code != 0 ||
                        context.GetData(end, (IntPtr)(&finish), sizeof(ulong), Vortice.Direct3D11.AsyncGetDataFlags.DoNotFlush).Code != 0)
                    {
                        Require(timeout.Elapsed.TotalSeconds < 5, "GPU timestamp readback timed out.");
                        Thread.Sleep(1);
                    }
                    if (i < 10) continue;
                    cpu.Add(recordMs); draws += world.ShadowDrawCount;
                    if (!clock.Disjoint && clock.Frequency > 0 && finish >= begin) gpu.Add((finish - begin) * 1000.0 / clock.Frequency);
                }
                Require(gpu.Count > 0, "All GPU samples were disjoint.");
                gpu.Sort();
                Console.WriteLine($"[ShadowBenchmark] {mode}: 600 boxes / 6 faces / 1024px; CPU record {cpu.Average():F3} ms; GPU mean {gpu.Average():F3} ms p95 {gpu[(int)((gpu.Count - 1) * .95)]:F3} ms ({gpu.Count} valid); {draws / cpu.Count:F0} draws/frame.");
            }
            world.ShadowCachingEnabled = world.ShadowCullingEnabled = true;
        }
    }

    private static int CountDarker(uint[] bright, uint[] dark)
        => bright.Zip(dark).Count(p => Brightness(p.First) > Brightness(p.Second) + 30);
    private static int Brightness(uint p) => (int)((p & 255) + ((p >> 8) & 255) + ((p >> 16) & 255));
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }

    internal static void SavePng(uint[] pixels, int width, int height, string path)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            uint p = pixels[y * width + x];
            bitmap.SetPixel(x, y, Color.FromArgb(255, (int)((p >> 16) & 255), (int)((p >> 8) & 255), (int)(p & 255)));
        }
        bitmap.Save(path, ImageFormat.Png);
    }
}
