using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Engine.Core.Diagnostics;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class TextureMipChecks
{
    internal static void Cpu(string root)
    {
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks++; }
        long fullBytes = 0, tailBytes = 0, fullPayload = 0, tailPayload = 0;
        var seen = new HashSet<string>();
        string? fixture = null; PreparedTexture? textureFixture = null;
        foreach (var entry in package.Manifest.Models)
        {
            string path = AssetPackage.Resolve(package.Root, entry.Path);
            var full = package.Load(path); var tail = package.LoadMipTail(path, 3);
            Check(full.Parts.Count == tail.Parts.Count, "Part count changed.");
            foreach (var pair in full.Parts.Zip(tail.Parts))
            {
                Check(pair.First.Positions.SequenceEqual(pair.Second.Positions) && pair.First.Indices.SequenceEqual(pair.Second.Indices) &&
                    pair.First.PartKey == pair.Second.PartKey && pair.First.EmissiveFactor == pair.Second.EmissiveFactor, "Mip request changed geometry/material identity.");
                var a = new[] { pair.First.BaseColorTexture, pair.First.MaterialTexture, pair.First.NormalTexture, pair.First.EmissiveTexture };
                var b = new[] { pair.Second.BaseColorTexture, pair.Second.MaterialTexture, pair.Second.NormalTexture, pair.Second.EmissiveTexture };
                for (int i = 0; i < a.Length; i++)
                {
                    if (a[i] is not { } t) { Check(b[i] == null, "Unexpected texture."); continue; }
                    var expected = t.MipTail(3); var actual = b[i]!;
                    Check(actual.Width == expected.Width && actual.Height == expected.Height && actual.FirstMip == expected.FirstMip &&
                        actual.Semantic == expected.Semantic && actual.Mips.Length == expected.Mips.Length &&
                        actual.Mips.Zip(expected.Mips).All(p => p.First.SequenceEqual(p.Second)), "Mip bytes changed.");
                    if (!seen.Add(t.Key)) continue;
                    string cache = Path.Combine(TextureCooker.CacheDirectory(path), t.Key + ".hs2tex");
                    Check(TextureCooker.TryReadMipTail(cache, t.Key, t.Semantic, 0, out var readFull, out long fb), "Full read failed.");
                    Check(TextureCooker.TryReadMipTail(cache, t.Key, t.Semantic, 3, out var readTail, out long tb), "Tail read failed.");
                    fullBytes += readFull!.Bytes; tailBytes += readTail!.Bytes; fullPayload += fb; tailPayload += tb;
                    if (t.Mips.Length > 4) { fixture = cache; textureFixture = t; }
                }
            }
        }
        Check(tailBytes < fullBytes / 8 && tailPayload < fullPayload / 4, "No substantial payload reduction.");
        var plan = MipResidencyBudget.Plan([new([1024, 256, 64, 16], 0, 10, true), new([1024, 256, 64, 16], 0, 1, false)], 1300);
        Check(plan.Mips.SequenceEqual(new[] { 0, 1 }) && !plan.OverBudget, "Budget demotion/pinning failed.");
        Check(MipResidencyBudget.Plan([new([1024, 256], 1, 1, true)], 1).OverBudget, "Impossible pinned budget hidden.");
        Check(MipResidencyBudget.DistanceMip(13, 0) == 0 && MipResidencyBudget.DistanceMip(16, 0) == 1 && MipResidencyBudget.DistanceMip(11, 1) == 0, "Hysteresis failed.");
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "texture-mips-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        string broken = Path.Combine(folder, "corrupt.hs2tex");
        byte[] bytes = File.ReadAllBytes(fixture!);
        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream))
        {
            reader.ReadUInt32(); reader.ReadInt32(); reader.ReadString();
            reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt32();
            bytes[stream.Position] ^= 1; // Corrupt only the first mip's digest.
        }
        File.WriteAllBytes(broken, bytes);
        Check(!TextureCooker.TryRead(broken, textureFixture!.Key, textureFixture.Semantic, out _), "Full read accepted bad high-detail digest.");
        Check(TextureCooker.TryReadMipTail(broken, textureFixture.Key, textureFixture.Semantic, 3, out _, out _), "Skipped mip must not be decoded.");
        bytes[^1] ^= 127; File.WriteAllBytes(broken, bytes);
        Check(!TextureCooker.TryReadMipTail(broken, textureFixture.Key, textureFixture.Semantic, 3, out _, out _), "Bad resident mip accepted.");
        File.WriteAllBytes(broken, bytes[..12]);
        Check(!TextureCooker.TryReadMipTail(broken, textureFixture.Key, textureFixture.Semantic, 3, out _, out _), "Truncation accepted.");
        bool rejected = false;
        try { TextureCooker.TryReadMipTail(broken, textureFixture.Key, textureFixture.Semantic, -1, out _, out _); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid mip accepted.");
        string telemetry = Path.Combine(folder, "telemetry.json");
        var profile = new LoadProfile(telemetry);
        profile.ObserveFrame(.3, .1f, false, false, false);
        profile.ObserveFrame(.016, .016f, true, true, true);
        profile.Finish("cpu-validation-complete", null);
        using var json = JsonDocument.Parse(File.ReadAllBytes(telemetry));
        Check(json.RootElement.GetProperty("ClampedFrames").GetInt32() == 1 &&
            Math.Abs(json.RootElement.GetProperty("ClampedAwayMilliseconds").GetDouble() - 200) < .01, "Clock diagnostics changed simulation or miscounted clamp.");
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new { checks, Textures = seen.Count, fullBytes, tailBytes, fullPayload, tailPayload }));
        Console.WriteLine($"PASS {checks} mip checks; {seen.Count} unique textures; CPU mip bytes {fullBytes} -> {tailBytes}; compressed payload read {fullPayload} -> {tailPayload}. {folder}");
    }

    internal static void Gpu(string root)
    {
        var package = new AssetPackage(LoadingPackageChecks.Build(root));
        string path = AssetPackage.Resolve(package.Root, package.Manifest.Models.First(m => m.Path.EndsWith("WoodenCratePbr.glb", StringComparison.OrdinalIgnoreCase)).Path);
        using var host = new EngineHost(640, 480, "Mip residency regression");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice, renderer.WorldOutputDescription, "Shaders");
        world.AmbientStrength = .5f; world.DirectionalStrength = 1;
        var source = package.Load(path);
        var bounds = source.Parts.SelectMany(p => p.Positions).ToArray();
        Vector3 min = bounds.Aggregate(Vector3.Min), max = bounds.Aggregate(Vector3.Max);
        var transform = Matrix4x4.CreateTranslation(-(min + max) / 2) * Matrix4x4.CreateScale(2 / (max - min).Length());
        var eye = new Vector3(0, .5f, -2);
        var vp = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(1, target.Width / (float)target.Height, .1f, 100);
        bool fail = false;
        var stream = new MipModelResidency(m => fail ? throw new IOException("Injected read failure") : package.LoadMipTail(path, m));
        uint[] Draw(RenderModel model) => target.Frame(() => { world.BeginFrame(); world.UpdateCamera(vp, eye); world.UpdatePointLights([], eye); world.DrawModel(renderer.CommandList, model, transform, Vector4.One); });
        void Ready(int mip)
        {
            stream.Request(mip); var clock = Stopwatch.StartNew();
            while (stream.CurrentMip != mip || stream.Busy)
            {
                target.Frame(() => { world.BeginFrame(); stream.Pump(world, renderer); if (stream.Model != null) { world.UpdateCamera(vp, eye); world.UpdatePointLights([], eye); world.DrawModel(renderer.CommandList, stream.Model, transform, Vector4.One); } }, read: false);
                if (stream.Error.Length > 0 || clock.Elapsed.TotalSeconds > 30) throw new InvalidDataException(stream.Error + " / mip readiness timeout");
                Thread.Sleep(1);
            }
            renderer.DrainRetiredResources();
        }
        Ready(3); long reduced = world.TextureResidentBytes;
        for (int cycle = 0; cycle < 8; cycle++) { Ready(0); Ready(3); }
        if (world.TextureResidentBytes != reduced) throw new InvalidDataException("Mip replacement leaked textures.");
        Ready(0); long full = world.TextureResidentBytes; uint[] actual = Draw(stream.Model!);
        using (var reference = world.CreateRenderModel(source))
        {
            uint[] expected = Draw(reference); int difference = 0;
            for (int i = 0; i < actual.Length; i++) for (int shift = 0; shift < 24; shift += 8)
                difference = Math.Max(difference, Math.Abs((int)(actual[i] >> shift & 255) - (int)(expected[i] >> shift & 255)));
            if (difference > 1 || actual.Distinct().Take(100).Count() < 100) throw new InvalidDataException("Full-resolution restoration changed pixels.");
        }
        fail = true; var old = stream.Model; stream.Request(2);
        var wait = Stopwatch.StartNew();
        do { target.Frame(() => { world.BeginFrame(); stream.Pump(world, renderer); }, false); Thread.Sleep(1); }
        while (stream.Busy && wait.Elapsed.TotalSeconds < 10);
        if (!ReferenceEquals(old, stream.Model) || stream.Error.Length == 0) throw new InvalidDataException("Failed replacement lost current model.");
        fail = false; stream.Retry(); Ready(2);
        stream.Request(3); target.Frame(() => { world.BeginFrame(); stream.Pump(world, renderer); }, false);
        stream.Request(0); Ready(0); // Superseded requests must not win.
        stream.Retire(world, renderer); renderer.DrainRetiredResources();
        if (world.TextureResidentBytes != 0 || full <= reduced * 4) throw new InvalidDataException("Mip accounting/release failed.");
        using var releaseFailure = new ManualResetEventSlim();
        var supersededFailure = new MipModelResidency(m =>
        {
            if (m == 3) { releaseFailure.Wait(); throw new IOException("Superseded failure"); }
            return package.LoadMipTail(path, m);
        });
        supersededFailure.Request(3);
        target.Frame(() => { world.BeginFrame(); supersededFailure.Pump(world, renderer); }, false);
        supersededFailure.Request(0); releaseFailure.Set(); wait.Restart();
        while (supersededFailure.CurrentMip != 0)
        {
            target.Frame(() => { world.BeginFrame(); supersededFailure.Pump(world, renderer); }, false);
            if (supersededFailure.Error.Length != 0 || wait.Elapsed.TotalSeconds > 10)
                throw new InvalidDataException("Superseded failure blocked the current request.");
            Thread.Sleep(1);
        }
        supersededFailure.Retire(world, renderer); renderer.DrainRetiredResources();
        if (world.TextureResidentBytes != 0) throw new InvalidDataException("Superseded failure leaked textures.");
        Console.WriteLine($"PASS GPU mip replacement: exact full-detail restoration, eight promotion/demotion cycles, failure retention, retry, superseded success/failure requests, zero texture bytes after retirement; {full} -> {reduced} resident bytes.");
    }
}
