using System.Numerics;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;

internal static class PbrMaterialChecks
{
    private static int _checks;
    internal static void Check(bool value, string message)
    { if (!value) throw new InvalidDataException(message); _checks++; }

    internal static LoadedModelPart Quad(float metal = 0, bool normals = true, bool uv = false)
        => new([new(-1,-1,0), new(1,-1,0), new(1,1,0), new(-1,1,0)],
            normals ? [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ] : null,
            uv ? [Vector2.Zero, Vector2.UnitX, Vector2.One, Vector2.UnitY] : null,
            [0,1,2,0,2,3], new(.6f,.3f,.1f,1), null, null, metal, .5f);

    public static void Run()
    {
        var legacy = StructuredData.ReadJson<LevelFile>(Encoding.UTF8.GetBytes("{}"));
        Check(legacy.LightingModel == SurfaceLightingModel.Legacy, "Missing mode must remain Legacy.");
        Check(!Encoding.UTF8.GetString(StructuredData.WriteJson(legacy)).Contains("LightingModel"), "Legacy bake hash changed by a default mode field.");
        legacy.LightingModel = SurfaceLightingModel.Pbr;
        Check(StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(legacy)).LightingModel == SurfaceLightingModel.Pbr, "Lighting mode lost in structured serialization.");
        string folder = Path.Combine(Path.GetTempPath(), "hs2-pbr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var part = Quad();
            Array.Fill(part.Normals!, Vector3.Normalize(new(1,1,1)));
            string path = Path.Combine(folder, "normal.glb");
            PbrTestRoom.WriteGlb(path, part, .7f, .4f, part.Color, new(2,1,.5f));
            var raw = GlbModelLoader.Load(path).Parts[0];
            Check(Vector3.Distance(raw.Normals![0], Vector3.Normalize(new(2,1,.5f))) < 1e-5f, "Legacy normal changed.");
            Check(Vector3.Distance(raw.PbrNormals![0], Vector3.Normalize(new(.5f,1,2))) < 1e-5f, "PBR glTF inverse-transpose normal incorrect.");
            TextureCooker.CookModel(path);
            long hits = CookedModelCache.CacheHits;
            var cooked = TextureCooker.LoadForRendering(path).Parts[0];
            Check(CookedModelCache.CacheHits == hits + 1 && cooked.PbrNormals!.SequenceEqual(raw.PbrNormals), "Cooked PBR normals lost.");
            string levelPath = Path.Combine(folder, "mode.json");
            LevelIO.Save(levelPath, legacy);
            Check(LevelIO.Load(levelPath).LightingModel == SurfaceLightingModel.Pbr, "RE:Dox level cache lost mode.");
            var editor = new Engine.Editor.Editor.LevelEditorController(); editor.LoadFromMemory(levelPath,legacy);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Engine.Editor.Editor.LevelEditorController).GetMethod("BeginEdit",flags)!.Invoke(editor,null);
            editor.LevelFile.LightingModel = SurfaceLightingModel.Legacy;
            typeof(Engine.Editor.Editor.LevelEditorController).GetMethod("EndEditIfAny",flags)!.Invoke(editor,null);
            Check(editor.Undo() && editor.LevelFile.LightingModel == SurfaceLightingModel.Pbr,"Undo lost lighting model.");
            Check(editor.Redo() && editor.LevelFile.LightingModel == SurfaceLightingModel.Legacy,"Redo lost lighting model.");
            Gpu();
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine($"PASS: {_checks} material checks (legacy defaults, level/cache persistence, node normals, scalar/no-UV/no-normal GPU paths).");
    }

    private static void Gpu()
    {
        using var host = new EngineHost(640,480,"Material validation");
        var renderer = host.Context.Renderer;
        using var world = new BasicWorldRenderer(renderer.GraphicsDevice, renderer.WorldOutputDescription, "Shaders");
        using var output = new PbrBaselineChecks.ImageTarget(renderer);
        world.LightingModel = SurfaceLightingModel.Pbr;
        world.AmbientStrength = world.DirectionalStrength = 0;
        using var scalar = world.CreateRenderModel(new([Quad()]));
        using var noNormal = world.CreateRenderModel(new([Quad(normals:false)]));
        using var withUv = world.CreateRenderModel(new([Quad(uv:true)]));
        using var metal = world.CreateRenderModel(new([Quad(1)]));
        var eye = new Vector3(0,0,3);
        var vp = Matrix4x4.CreateLookAt(eye,Vector3.Zero,Vector3.UnitY) * Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI/3,output.Width/(float)output.Height,.05f,100);
        uint[] Frame(RenderModel model, float intensity, Matrix4x4? transform = null)
            => output.Frame(() =>
            {
                world.BeginFrame(); world.UpdateCamera(vp,eye);
                world.UpdatePointLights([new(new(0,0,2),Vector3.One,intensity,10)],eye);
                using(world.BatchObjectUploads()) world.DrawModel(renderer.CommandList,model,transform ?? Matrix4x4.Identity,Vector4.One);
            });
        static int Bright(uint p) => (int)((p&255)+((p>>8)&255)+((p>>16)&255));
        var on = Frame(scalar,3); var off = Frame(scalar,0);
        Check(on.Zip(off).Count(p=>Bright(p.First)>Bright(p.Second)+30)>10000, "Scalar/no-UV model did not respond to light.");
        Check(Frame(noNormal,3).SequenceEqual(on), "Flat generated normals differ from authored quad normals.");
        Check(Frame(withUv,3).SequenceEqual(on), "Missing texture fallback depends on presence of UVs.");
        world.AmbientStrength = .1f; world.DirectionalStrength = 1;
        var towardSun = Matrix4x4.CreateRotationY(MathF.PI);
        Check(!Frame(metal,0,towardSun).SequenceEqual(Frame(scalar,0,towardSun)), "Neutral MR fallback erased scalar metallic.");
        world.AmbientStrength = world.DirectionalStrength = 0;
        var mirror = Frame(noNormal,3,Matrix4x4.CreateScale(-1,.7f,2));
        Check(mirror.Count(p=>Bright(p)>50)>10000, "Negative/nonuniform instance scale lost surface lighting.");
        Check(world.ObjectUploadCount == 1, "PBR material changes broke batched uploads.");
    }
}
