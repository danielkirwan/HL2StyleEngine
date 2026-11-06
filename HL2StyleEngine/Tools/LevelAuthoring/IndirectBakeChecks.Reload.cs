using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Engine.Core.Assets;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Veldrid;

internal static partial class IndirectBakeChecks
{
    internal static void RunReload()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "lighting-reload-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "fixture.json"), modelPath = Path.Combine(folder, "cube.glb");
        string cachePath = LightmapFile.PathFor(path);
        WriteCube(modelPath);
        byte[] modelBytes = File.ReadAllBytes(modelPath);
        string Resolve(string p) => Path.Combine(folder, p);
        var level = Fixture("cube.glb");
        var data = IndirectLightBaker.Bake(BakeSnapshot.Capture(level, Resolve));
        LightmapFile.Save(cachePath, data);
        byte[] valid = File.ReadAllBytes(cachePath);
        var editor = new LevelEditorController();
        void Reload() => editor.LoadFromMemory(path, StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(level)));
        Reload();
        using var host = new EngineHost(640, 480, "Diffuse reload checks");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice, renderer.WorldOutputDescription, "Shaders")
        { AmbientStrength = 0, DirectionalStrength = 0 };
        using var session = new LevelBakeSession();
        var source = GlbModelLoader.Load(modelPath);
        using var model = world.CreateRenderModel(source);
        AssetFingerprintReader? fingerprints = null;
        LevelLightState? lightState = null;
        LightmapBinding? firstBinding = null;
        object? Read(string field) => typeof(LevelBakeSession).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session);
        object? Atlas() => Read("_resources")?.GetType().GetField("Atlas")!.GetValue(Read("_resources"));
        uint[] Frame(bool read = false) => target.Frame(() =>
        {
            world.BeginFrame();
            session.Update(editor, Resolve, world, renderer, lightState, fingerprints);
            var eye = new Vector3(-1, 1.5f, 0);
            world.UpdateCamera(Matrix4x4.CreateLookAt(eye, new(-1, 0, 1), Vector3.UnitY) *
                Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 640f / 480, .05f, 50), eye);
            world.UpdatePointLights([], eye);
            world.RenderShadows(renderer, [], new Viewport(0, 0, 640, 480, 0, 1));
            using (world.BatchObjectUploads())
                foreach (var record in data.Records)
                {
                    var entity = editor.LevelFile.Entities.Single(e => e.Id == record.EntityId);
                    var binding = session.GetBinding(entity, source, record.Transform, editor.LevelFile, world, renderer);
                    if (record == data.Records[0]) firstBinding = binding;
                    world.DrawModel(renderer.CommandList, model, record.Transform, Vector4.One, null, null, binding);
                }
        }, read);
        void Settle(bool ready = true)
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Frame();
                if (!session.Busy && (!ready || session.ReadyModels == data.Records.Count)) break;
                if (timer.Elapsed.TotalSeconds > 20) throw new TimeoutException(session.Status);
                Thread.Sleep(2);
            } while (true);
            Check(session.Ready == ready, "Unexpected diffuse readiness: " + session.Status);
            renderer.DrainRetiredResources();
        }
        Settle();
        var payload = Read("_data"); var atlas = Atlas(); var bindingBefore = firstBinding;
        uint[] before = Frame(true);
        Check(before.Distinct().Count() > 32, "Reload reference is blank.");
        for (int i = 0; i < 6; i++)
        {
            Reload(); Settle();
            Check(ReferenceEquals(payload, Read("_data")), "Unchanged reload decoded another payload.");
            Check(ReferenceEquals(atlas, Atlas()), "Unchanged reload uploaded another GPU atlas.");
            Check(!ReferenceEquals(bindingBefore, firstBinding), "Reload retained old model bindings.");
            Check(bindingBefore!.Bytes == 0, "Retired binding buffers were not released.");
            bindingBefore = firstBinding;
        }
        uint[] after = Frame(true);
        Check(before.AsSpan().SequenceEqual(after), "Unchanged reload changed rendered pixels.");
        RenderPipelineChecks.SavePng(before, 640, 480, Path.Combine(folder, "before.png"));
        RenderPipelineChecks.SavePng(after, 640, 480, Path.Combine(folder, "after.png"));

        source = GlbModelLoader.Load(modelPath); Frame(); Settle();
        Check(!ReferenceEquals(bindingBefore, firstBinding) && firstBinding!.UsesSource(source), "Bindings reused a replaced model instance.");
        Check(ReferenceEquals(atlas, Atlas()), "Changing only the model instance replaced the atlas.");

        var timestamp = File.GetLastWriteTimeUtc(cachePath);
        var broken = (byte[])valid.Clone(); broken[^1] ^= 0x40;
        File.WriteAllBytes(cachePath, broken); File.SetLastWriteTimeUtc(cachePath, timestamp);
        Reload(); Settle(false);
        Check(Atlas() == null && session.ResidentBytes == 0, "Corrupt cache retained GPU lighting.");
        File.WriteAllBytes(cachePath, valid); Reload(); Settle();
        payload = Read("_data"); atlas = Atlas();

        // Same source key, genuinely different valid pixels: never trust the key alone.
        data.Pixels.AsSpan().Clear(); LightmapFile.Save(cachePath, data);
        Reload(); Settle();
        Check(!ReferenceEquals(payload, Read("_data")) && !ReferenceEquals(atlas, Atlas()), "Changed valid cache reused the old atlas.");
        Check(((LightmapData)Read("_data")!).Pixels.All(v => v == 0), "Changed pixels were not loaded.");
        File.WriteAllBytes(cachePath, valid); Reload(); Settle();

        level.Entities[^1].Intensity += 1; Reload(); Settle(false);
        Check(Atlas() == null, "Changed light left stale lighting active.");
        level = Fixture("cube.glb"); Reload(); Settle();
        var modelTime = File.GetLastWriteTimeUtc(modelPath);
        var changedModel = (byte[])modelBytes.Clone(); changedModel[^1] ^= 1;
        File.WriteAllBytes(modelPath, changedModel); File.SetLastWriteTimeUtc(modelPath, modelTime);
        Reload(); Settle(false);
        Check(Atlas() == null, "Same-size/timestamp model change did not invalidate the bake.");
        File.WriteAllBytes(modelPath, modelBytes); Reload(); Settle();

        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            fingerprints = new AssetFingerprintReader(immutableIdentity: _ =>
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("QA worker release timed out.");
                return null;
            });
            Reload(); Frame();
            Task running = (Task)Read("_job")!;
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "Validation worker did not start.");
                Check(!session.Ready && session.Busy, "Unvalidated payload was exposed to the new revision.");
                session.Cancel();
            }
            finally { release.Set(); }
            try { running.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            fingerprints = null; Frame(); renderer.DrainRetiredResources();
            Check(!session.Ready && Atlas() == null && session.ResidentBytes == 0, "Cancelled validation retained resources.");
        }
        Reload(); Settle();
        File.Delete(cachePath); Reload(); Settle(false);
        Check(Atlas() == null, "Missing cache retained resources.");
        File.WriteAllBytes(cachePath, valid); Reload(); Settle();
        // Returning from a different level must not use a global cache of old levels.
        var other = Fixture("cube.glb"); other.Bake.Enabled = false;
        editor.LoadFromMemory(Path.Combine(folder, "other.json"), other); Settle(false);
        Check(session.ResidentBytes == 0, "Level change retained the old atlas.");
        Reload(); Settle();

        level.LightingScenarios = [
            new() { Id = "on", Lights = [new() { EntityId = "light", Enabled = true }] },
            new() { Id = "off", Lights = [new() { EntityId = "light", Enabled = false }] }
        ];
        data = IndirectLightBaker.Bake(BakeSnapshot.Capture(LightingScenarioResolver.CreateView(level, level.LightingScenarios[0]), Resolve));
        string scenarioCache = LightmapFile.PathFor(LightingScenarioResolver.PathFor(path, "on"));
        LightmapFile.Save(scenarioCache, data);
        byte[] scenarioValid = File.ReadAllBytes(scenarioCache);
        lightState = new(); Reload(); Settle();
        payload = Read("_data"); atlas = Atlas();
        uint[] scenarioBefore = Frame(true);
        for (int i = 0; i < 4; i++)
        {
            lightState.Restore([new() { EntityId = "light", Enabled = false }]); Frame();
            Check(!session.Ready && session.ActiveResidentBytes == 0, "Off state retained active diffuse light.");
            lightState.Clear(); uint[] immediate = Frame(true);
            Check(session.Ready && !session.Busy && ReferenceEquals(payload, Read("_data")) && ReferenceEquals(atlas, Atlas()),
                "Warm same-revision switch unnecessarily revalidated or uploaded its bank.");
            Check(scenarioBefore.AsSpan().SequenceEqual(immediate), "Warm first frame lost indirect light.");
        }
        lightState.Restore([new() { EntityId = "light", Enabled = false }]); Frame();
        var scenarioCorrupt = (byte[])scenarioValid.Clone(); scenarioCorrupt[^1] ^= 1;
        File.WriteAllBytes(scenarioCache, scenarioCorrupt);
        File.SetLastWriteTimeUtc(scenarioCache, DateTime.UtcNow.AddSeconds(10));
        lightState.Clear(); Settle(false);
        Check(session.ResidentBytes == 0, "Changed invalid alternate cache retained resources.");
        File.WriteAllBytes(scenarioCache, scenarioValid); Reload(); Settle();
        lightState.Restore([new() { EntityId = "light", Enabled = false }]); Frame();
        level.Entities[^1].Intensity += 1; Reload(); lightState.Clear(); Settle(false);
        Check(session.ResidentBytes == 0, "New authored revision reused a warm stale scenario.");
        session.Dispose(); renderer.DrainRetiredResources();
        Check(Read("_validated") == null && Read("_data") == null && session.ResidentBytes == 0, "Disposal retained the payload.");
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new
        { Checks = _checks, ExactReloadPixels = true, SourceLevelsModified = false, Note = "Automated isolated GPU/lifetime test, not a frame-pacing benchmark." },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: {_checks} diffuse reload/identity/cancellation/GPU ownership checks: {folder}");
    }
}
