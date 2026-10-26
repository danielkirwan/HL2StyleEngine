using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Engine.Core.Serialization;
using Engine.Editor.Editor;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Entities;
using Engine.Runtime.Hosting;

internal static class FlashlightMountChecks
{
    internal static void Run(string root, bool verify)
    {
        using var host = new EngineHost(1280, 720, "Flashlight mount checks");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var game = new Game.HL2GameModule(Path.Combine(root, "Game/Content/Levels", ShadowStabilityChecks.FileName));
        game.AssetStreamingEnabled = false;
        game.Initialize(host.Context);
        var camera = (Game.FpsCamera)PbrBaselineChecks.Field(game, "_camera");
        var editor = (LevelEditorController)PbrBaselineChecks.Field(game, "_editor");
        var world = (BasicWorldRenderer)PbrBaselineChecks.Field(game, "_world");
        var rooms = (ReflectionProbeSession)PbrBaselineChecks.Field(game, "_roomReflections");
        var bake = (LevelBakeSession)PbrBaselineChecks.Field(game, "_bakedLighting");
        var entities = (List<Entity>)PbrBaselineChecks.Field(game, "_runtimeEntities");
        var models = (IDictionary)PbrBaselineChecks.Field(game, "_weaponModelCache");
        byte[] authored = StructuredData.WriteJson(editor.LevelFile);
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "flashlight-mount-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        world.ViewCullingEnabled = false;
        var wait = Stopwatch.StartNew();
        while (true)
        {
            target.Frame(() => game.RenderWorld(renderer), read: false);
            if (models.Count > 0 && models.Values.Cast<object>().All(m => PbrBaselineChecks.Field(m, "Model") != null) &&
                rooms.Ready && rooms.AlternateStateReady && bake.Ready &&
                bake.ReadyModels == editor.LevelFile.Entities.Count(e => e.StaticLighting && !e.ExcludeFromDiffuseBake)) break;
            if (wait.Elapsed.TotalSeconds > 90) throw new TimeoutException(rooms.Status + " / " + bake.Status);
            Thread.Sleep(5);
        }

        var crate = entities.Single(e => e.Name == "SRPuzzle_UtilityWeightCrate");
        var rail = entities.Single(e => e.Name == "CentralHall_DispatchDesk");
        var start = crate.Transform.Position;
        PbrBaselineChecks.Set(game, "_flashlightOn", false);
        camera.Position = start + new Vector3(-1, 1.16f, -1.4f); Aim(start);
        var fixturesBefore = Frame("fixtures-only-before");
        Check(world.ShadowPointLightCount == 14, "Fixture shadow coverage changed.");
        PbrBaselineChecks.Set(game, "_flashlightOn", true);
        Frame("crate-close-with-fixtures");
        Call("UseLightSwitch", entities.Single(e => e.Name == "Fixture Power Switch"));
        foreach (var (name, subject, offset) in new[]
        {
            ("crate-front", crate, new Vector3(0,1.16f,-1.4f)),
            ("crate-side", crate, new Vector3(-1.4f,1.16f,0)),
            ("crate-down", crate, new Vector3(-.6f,1.16f,-.6f)),
            ("railing-down", rail, new Vector3(-1.5f,1.25f,-1)),
            ("railing-oblique", rail, new Vector3(1.5f,1.25f,1)),
        })
        {
            camera.Position = subject.Transform.Position + offset; Aim(subject.Transform.Position);
            var shadow = Frame(name);
            world.ShadowCachingEnabled = world.ShadowCullingEnabled = false;
            var reference = Frame(name + "-reference");
            world.ShadowCachingEnabled = world.ShadowCullingEnabled = true;
            SamePixels(shadow, reference, name);
            Check(world.ShadowSpotLightCount == 1 && world.ShadowPointLightCount == 0, "Flashlight lost its independent shadow.");
            editor.LevelFile.EnableShadows = false;
            var plain = Frame(name + "-unshadowed");
            editor.LevelFile.EnableShadows = true;
            int darker = shadow.Zip(plain).Count(p => Energy(p.Second) > Energy(p.First) + 15);
            Console.WriteLine($"{name}: {darker} shadow pixels; source offset {Vector3.Distance(Torch().Position,camera.Position):F4}m.");
        }

        // Use the real gravity-pickup path; only the deterministic hold poses bypass simulation.
        camera.Position = start + new Vector3(0, 1.16f, -3.5f); Aim(start);
        Call("PickUp", crate, true);
        Check(crate.IsHeld && ReferenceEquals(PbrBaselineChecks.Field(game, "_held"), crate), "Gravity pickup did not hold the test crate.");
        try
        {
            foreach (float pitch in new[] { -.8f, 0f, .65f })
            {
                camera.Pitch = pitch;
                var center = camera.Position + camera.Forward * 2;
                Call("SetPhysicsBodyCenter", crate, center);
                crate.Transform.Position = center;
                var shadow = Frame("held-" + pitch);
                var casters = (List<ShadowCaster>)PbrBaselineChecks.Field(game, "_shadowCasters");
                Check(casters.Any(c => c.Center == center && c.Model != null), "Held model is missing from the shadow caster geometry.");
                world.ShadowCachingEnabled = world.ShadowCullingEnabled = false;
                SamePixels(shadow, Frame("held-" + pitch + "-reference"), "held " + pitch);
                world.ShadowCachingEnabled = world.ShadowCullingEnabled = true;
                Check(world.ShadowSpotLightCount == 1, "Holding a prop disabled the flashlight shadow.");
            }
        }
        finally
        {
            Call("DropHeld");
            Call("SetPhysicsBodyCenter", crate, start);
            crate.Transform.Position = start;
        }
        Check(!crate.IsHeld, "Test prop did not release.");

        var saved = entities.ToArray();
        entities.Clear();
        try
        {
            camera.Position = new(2, 3, 4);
            foreach (float yaw in new[] { -3f, -1f, 0f, 2f })
            foreach (float pitch in new[] { -1.5f, -.8f, 0f, .8f, 1.5f })
            {
                camera.Yaw = yaw; camera.Pitch = pitch;
                var up = Vector3.Normalize(Vector3.Cross(camera.Right, camera.Forward));
                var light = Torch();
                var delta = light.Position - camera.Position;
                if (verify)
                {
                    Check(Math.Abs(Vector3.Dot(delta,camera.Right)-.04f) < .00001f &&
                        Math.Abs(Vector3.Dot(delta,up)+.02f) < .00001f && Math.Abs(Vector3.Dot(delta,camera.Forward)) < .00001f,
                        "Flashlight offset does not follow the camera basis.");
                }
                Check(light.Intensity == 7 && light.Range == 22 && light.SpotAngleDegrees == 55 && light.CastShadows,
                    "Flashlight illumination or shadow settings changed.");
                Check(Vector3.Dot(light.Direction,Vector3.Normalize(camera.Position+camera.Forward*8-light.Position)) > .99999f,
                    "Illumination aim and source disagree.");
            }
            camera.Position = Vector3.Zero; camera.Yaw = camera.Pitch = 0;
            var wall = new Entity("mount-wall", EntityTypes.RigidBody, "Wall");
            wall.Collider.Shape = RuntimeShapeKind.Box; wall.Collider.Size = new(.002f, 2, 2);
            wall.Render.Size = new(.0001f);
            entities.Add(wall);
            float previous = 0;
            for (int step = 0; step <= 60; step++)
            {
                float surface = step * .001f;
                wall.Transform.Position = new(-surface-.001f,0,0);
                var source = Torch().Position;
                float displacement = source.Length();
                Check(-source.X <= surface + .00001f, "Flashlight crossed the wall.");
                if (verify) Check(displacement >= previous-.00001f && displacement-previous < .0015f, "Wall clamp jumps at its boundary.");
                previous = displacement;
            }
            wall.Transform.Position = Vector3.Zero;
            Check(Torch().Position == camera.Position, "Camera inside a collider did not clamp the light.");
            entities.Clear();
            if (verify) Check(Math.Abs(Torch().Position.Length()-MathF.Sqrt(.002f)) < .00001f, "Free-space mount did not recover.");
            editor.LevelFile.OffsetFlashlight = false;
            Check(Torch().Position == camera.Position && Torch().Direction == camera.Forward, "Legacy eye light changed.");
            editor.LevelFile.OffsetFlashlight = true;
        }
        finally { entities.Clear(); entities.AddRange(saved); }
        Call("UseLightSwitch", entities.Single(e => e.Name == "Fixture Power Switch"));
        camera.Position = start + new Vector3(-1, 1.16f, -1.4f); Aim(start);
        PbrBaselineChecks.Set(game, "_flashlightOn", false);
        SamePixels(fixturesBefore, Frame("fixtures-only-after"), "fixture preservation");
        Check(StructuredData.WriteJson(editor.LevelFile).SequenceEqual(authored), "Checks modified authored level data.");
        Console.WriteLine($"PASS: flashlight close-up/held/reference checks{(verify ? ", 20 camera poses and continuous wall clamp" : " (diagnostic placement)")}. {folder}");

        WorldPointLight Torch() => (WorldPointLight)Call("CreateFlashlight")!;
        object? Call(string name, params object?[] args) => typeof(Game.HL2GameModule).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(game,args);
        void Aim(Vector3 point)
        {
            var direction = Vector3.Normalize(point-camera.Position);
            camera.Yaw = MathF.Atan2(direction.X,direction.Z); camera.Pitch = MathF.Asin(direction.Y);
        }
        uint[] Frame(string name)
        {
            var pixels = target.Frame(() => game.RenderWorld(renderer));
            RenderPipelineChecks.SavePng(pixels,target.Width,target.Height,Path.Combine(folder,name+".png"));
            return pixels;
        }
        void SamePixels(uint[] a, uint[] b, string label)
        {
            int max = a.Zip(b).Max(p => Enumerable.Range(0,3).Max(c => Math.Abs((int)((p.First>>(8*c))&255)-(int)((p.Second>>(8*c))&255))));
            Check(max <= 1, label+" differs from reference: "+max+"/255.");
        }
        static int Energy(uint p) => (int)((p&255)+((p>>8)&255)+((p>>16)&255));
        static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    }
}
