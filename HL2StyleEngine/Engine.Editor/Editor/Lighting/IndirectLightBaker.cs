using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Physics.Dynamics;
using Engine.Render;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using Half = System.Half;

namespace Engine.Editor.Editor;

public sealed record BakeSnapshot(byte[] Json, Dictionary<string, string> ModelPaths, int LightingVersion = 1)
{
    public static BakeSnapshot Capture(LevelFile level, Func<string, string> resolve)
    {
        byte[] json = StructuredData.WriteJson(level);
        // The distant specular test rig is not a contributor to this diffuse bake.
        if (level.ReflectionEnvironment != ReflectionPreset.None || level.ReflectionProbeId != null || level.AutomaticRoomReflections || level.Entities.Any(e => e.Type == EntityTypes.ReflectionProbe))
        {
            var diffuse = StructuredData.ReadJson<LevelFile>(json);
            diffuse.ReflectionEnvironment = ReflectionPreset.None;
            diffuse.ReflectionProbeId = null;
            diffuse.AutomaticRoomReflections = false;
            diffuse.Entities.RemoveAll(e => e.Type == EntityTypes.ReflectionProbe);
            json = StructuredData.WriteJson(diffuse);
        }
        return new(json, level.Entities.Where(e => e.StaticLighting && !string.IsNullOrWhiteSpace(e.MeshPath))
            .Select(e => e.MeshPath).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p, resolve, StringComparer.OrdinalIgnoreCase), level.Bake?.Probes == null ? 1 : 2);
    }

    public string Key(CancellationToken cancel = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"HS2-single-bounce-{LightingVersion}")); hash.AppendData(Json);
        foreach (var entry in ModelPaths.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            cancel.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Key));
            using var stream = File.OpenRead(entry.Value); hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public static partial class IndirectLightBaker
{
    private const float Bias = .002f;
    private sealed record Surface(MeshCollisionTriangle Triangle, Vector3 A, Vector3 B, Vector3 C,
        Vector2 Ua, Vector2 Ub, Vector2 Uc, Albedo Material, Vector3 Tint);
    private sealed class Albedo
    {
        public Vector3 Factor;
        public byte[]? Rgba;
        public int Width, Height;
        public float Metallic;
        public byte[]? MaterialRgba;
        public int MaterialWidth, MaterialHeight;
        public Vector3 Sample(Vector2 uv)
        {
            float metal = Metallic;
            if (MaterialRgba != null)
            {
                int mx = Math.Clamp((int)((uv.X-MathF.Floor(uv.X))*MaterialWidth),0,MaterialWidth-1);
                int my = Math.Clamp((int)((uv.Y-MathF.Floor(uv.Y))*MaterialHeight),0,MaterialHeight-1);
                metal *= MaterialRgba[(my*MaterialWidth+mx)*4+2]/255f;
            }
            Vector3 factor = Factor * (1-metal);
            if (Rgba == null) return factor;
            int x = Math.Clamp((int)((uv.X - MathF.Floor(uv.X)) * Width), 0, Width - 1);
            int y = Math.Clamp((int)((uv.Y - MathF.Floor(uv.Y)) * Height), 0, Height - 1);
            int index = (y * Width + x) * 4;
            return factor * new Vector3(Linear(Rgba[index] / 255f), Linear(Rgba[index + 1] / 255f), Linear(Rgba[index + 2] / 255f));
        }
    }

    public static bool IsStaticGeometry(LevelEntityDef entity)
        => entity.StaticLighting && entity.Type is EntityTypes.Box or EntityTypes.Prop or EntityTypes.RigidBody &&
           entity.MotionType == MotionType.Static && !entity.CanPickUp && !entity.Damageable && !entity.IsKinematic &&
           entity.Interaction == null && entity.Scripts.Count == 0 && !string.IsNullOrWhiteSpace(entity.MeshPath);

    public static LightmapData Bake(BakeSnapshot snapshot, Action<float>? progress = null, CancellationToken cancel = default)
    {
        string key = snapshot.Key(cancel);
        var level = StructuredData.ReadJson<LevelFile>(snapshot.Json);
        var settings = level.Bake ?? new();
        if (!settings.Enabled || settings.TexelsPerTriangle is < 4 or > 32 || settings.Samples is < 8 or > 256 ||
            !float.IsFinite(settings.RayDistance) || settings.RayDistance is < 1 or > 200)
            throw new InvalidDataException("Enable indirect lighting; use 4-32 texels, 8-256 samples and a 1-200 m ray distance.");
        if (level.Entities.Select(e => e.Id).Any(string.IsNullOrWhiteSpace) || level.Entities.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != level.Entities.Count)
            throw new InvalidDataException("Bake entities require unique non-empty IDs.");
        var editor = new LevelEditorController(); editor.LoadFromMemory("bake-snapshot.json", level);
        var controlled = level.Entities.Where(e => string.Equals(e.Interaction?.Kind, "LightSwitch", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => LevelLightState.Targets(level, e.Interaction!)).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lights = new List<WorldPointLight>(); var data = new LightmapData { Key = key, Texels = settings.TexelsPerTriangle };
        for (int i = 0; i < level.Entities.Count; i++)
        {
            var entity = level.Entities[i];
            if (entity.Type != EntityTypes.PointLight || !entity.BakeIndirect) continue;
            ValidateParents(entity, level);
            if (controlled.Contains(entity.Id) || entity.FlickerAmount != 0 || entity.Scripts.Count > 0 || entity.Interaction != null ||
                entity.MotionType != MotionType.Static || entity.CanPickUp || entity.IsKinematic)
                throw new InvalidDataException($"{entity.Name}: switch-controlled, flickering or scripted lights must remain realtime-only.");
            if (!level.UsePointLights || !entity.LightEnabled || !float.IsFinite(entity.Intensity) || entity.Intensity <= 0 ||
                !float.IsFinite(entity.Range) || entity.Range <= 0) continue;
            if (!editor.TryGetEntityWorldTRS(i, out var position, out var rotation, out _)) throw new InvalidDataException("Invalid light transform.");
            Vector4 color = entity.LightColor;
            if (!Finite(position) || !Finite(new(color.X,color.Y,color.Z)) || !float.IsFinite(entity.SpotAngleDeg)) throw new InvalidDataException("Invalid bake light values.");
            lights.Add(new(position, Vector3.Max(Vector3.Zero,new(color.X, color.Y, color.Z)), entity.Intensity, entity.Range,
                Vector3.Transform(-Vector3.UnitZ, rotation), entity.IsSpotLight && entity.SpotAngleDeg > 0 ? Math.Clamp(entity.SpotAngleDeg,5,150) : 0));
            data.LightIds.Add(entity.Id);
        }
        if (lights.Count is < 1 or > 32) throw new InvalidDataException("Select 1-32 enabled fixed lights with Bake Indirect enabled.");
        var surfaces = new List<Surface>();
        var models = new Dictionary<string, LoadedModel>(StringComparer.OrdinalIgnoreCase);
        var materials = new Dictionary<LoadedModelPart, Albedo>();
        for (int i = 0; i < level.Entities.Count; i++)
        {
            var entity = level.Entities[i];
            if (!entity.StaticLighting) continue;
            cancel.ThrowIfCancellationRequested(); ValidateParents(entity, level);
            if (!IsStaticGeometry(entity)) throw new InvalidDataException($"{entity.Name}: static lighting requires a fixed, non-interactive, non-destructible GLB model.");
            if (!models.TryGetValue(entity.MeshPath, out var model))
                models[entity.MeshPath] = model = GlbModelLoader.Load(snapshot.ModelPaths[entity.MeshPath]);
            var draw = editor.DrawBoxes[i];
            Matrix4x4 transform = BoundsFit(model, draw.Position, draw.Size, draw.Rotation);
            if (!Matrix4x4.Invert(transform, out var inverse)) throw new InvalidDataException("Degenerate model transform.");
            var normalTransform = Matrix4x4.Transpose(inverse);
            data.Records.Add(new() { EntityId = entity.Id, FirstTriangle = surfaces.Count,
                PartTriangles = model.Parts.Select(p => p.Indices.Length / 3).ToArray(), Transform = transform });
            foreach (var part in model.Parts)
            {
                int maxTriangles = (2048/(settings.TexelsPerTriangle+4))*(2048/(settings.TexelsPerTriangle+4));
                if (part.Indices.Length == 0 || part.Indices.Length % 3 != 0 || surfaces.Count + part.Indices.Length/3 > maxTriangles)
                    throw new InvalidDataException("Invalid topology or geometry exceeds the single-atlas budget. Reduce static geometry/texels per triangle.");
                bool pbr = level.LightingModel == SurfaceLightingModel.Pbr;
                if (!materials.TryGetValue(part, out var material)) materials[part] = material = ReadAlbedo(part, pbr);
                Vector4 color = entity.Color; Vector3 tint = new(color.X,color.Y,color.Z);
                if (!Finite(tint)) throw new InvalidDataException("Invalid model tint.");
                for (int n = 0; n < part.Indices.Length; n += 3)
                {
                    int a = (int)part.Indices[n], b = (int)part.Indices[n + 1], c = (int)part.Indices[n + 2];
                    var tri = new MeshCollisionTriangle(Vector3.Transform(part.Positions[a], transform),
                        Vector3.Transform(part.Positions[b], transform), Vector3.Transform(part.Positions[c], transform));
                    var normals = pbr ? part.PbrNormals ?? part.Normals : part.Normals;
                    var flat = pbr ? Vector3.Normalize(Vector3.TransformNormal(Vector3.Cross(part.Positions[b]-part.Positions[a],part.Positions[c]-part.Positions[a]),normalTransform)) : tri.Normal;
                    Vector3 Normal(int index) => normals == null ? flat : Vector3.Normalize(Vector3.TransformNormal(normals[index], normalTransform));
                    if (!Finite(tri.A) || !Finite(tri.B) || !Finite(tri.C) || !Finite(Normal(a)) || !Finite(Normal(b)) || !Finite(Normal(c)))
                        throw new InvalidDataException($"{entity.Name}: non-finite mesh position/normal.");
                    surfaces.Add(new(tri, Normal(a), Normal(b), Normal(c), part.TexCoords?[a] ?? default,
                        part.TexCoords?[b] ?? default, part.TexCoords?[c] ?? default, material, Vector3.Clamp(tint,Vector3.Zero,Vector3.One)));
                }
            }
        }
        if (surfaces.Count == 0) throw new InvalidDataException("Mark at least one fixed model Static Lighting.");
        int tile = settings.TexelsPerTriangle + 4, size = 32;
        while ((size / tile) * (size / tile) < surfaces.Count && size < 2048) size *= 2;
        if (surfaces.Count > (size / tile) * (size / tile)) throw new InvalidDataException("This first-pass bake exceeds one 2048-square atlas. Reduce static geometry or texels per triangle.");
        data.Size = size; data.Pixels = new byte[size * size * 8];
        var pixels = new Half[size * size * 4];
        var mesh = new MeshCollisionMesh(surfaces.Select(s => s.Triangle).ToArray(), 0);
        int done = 0, columns = size / tile;
        Parallel.For(0, surfaces.Count, new ParallelOptions { CancellationToken = cancel, MaxDegreeOfParallelism = 2 }, index =>
        {
            var surface = surfaces[index]; var candidates = new List<int>();
            int startX = index % columns * tile, startY = index / columns * tile;
            // Compute interior samples once, then extend edge values through each chart's guard band.
            var interior = new Vector3[settings.TexelsPerTriangle * settings.TexelsPerTriangle];
            for (int y = 0; y < settings.TexelsPerTriangle; y++) for (int x = 0; x < settings.TexelsPerTriangle - y; x++)
            {
                cancel.ThrowIfCancellationRequested();
                float u = x / (float)(settings.TexelsPerTriangle - 1), v = y / (float)(settings.TexelsPerTriangle - 1);
                Vector3 position = surface.Triangle.A * (1-u-v) + surface.Triangle.B * u + surface.Triangle.C * v;
                Vector3 normal = Vector3.Normalize(surface.A * (1-u-v) + surface.B * u + surface.C * v);
                // Pull edge samples slightly into their triangle to avoid tracing through adjacent faces.
                position = Vector3.Lerp(position, surface.Triangle.Center, .002f);
                uint seed = (uint)(index * 73856093 ^ x * 19349663 ^ y * 83492791);
                interior[y * settings.TexelsPerTriangle + x] = Integrate(position, normal, seed, settings, surfaces, mesh, lights, candidates);
            }
            for (int y = 0; y < tile; y++) for (int x = 0; x < tile; x++)
            {
                int ix = Math.Clamp(x - 2, 0, settings.TexelsPerTriangle - 1), iy = Math.Clamp(y - 2, 0, settings.TexelsPerTriangle - 1);
                if (ix + iy >= settings.TexelsPerTriangle)
                {
                    float factor = (settings.TexelsPerTriangle - 1f) / (ix + iy);
                    ix = (int)MathF.Round(ix * factor); iy = settings.TexelsPerTriangle - 1 - ix;
                }
                var value = interior[iy * settings.TexelsPerTriangle + ix]; int offset = ((startY+y)*size+startX+x)*4;
                pixels[offset]=(Half)value.X; pixels[offset+1]=(Half)value.Y; pixels[offset+2]=(Half)value.Z; pixels[offset+3]=(Half)1;
            }
            progress?.Invoke(Interlocked.Increment(ref done) / (float)surfaces.Count * (settings.Probes == null ? 1 : .85f));
        });
        MemoryMarshal.AsBytes(pixels.AsSpan()).CopyTo(data.Pixels);
        if (settings.Probes != null)
        {
            data.Probes = BakeProbes(settings, surfaces, mesh, lights, progress, cancel);
            data.PreparedProbes = new LightProbeSampler(data.Probes);
        }
        if (key != snapshot.Key(cancel)) throw new IOException("A source model changed while baking. Bake again.");
        return data;
    }

    private static void ValidateParents(LevelEntityDef entity, LevelFile level)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { entity.Id };
        while (!string.IsNullOrWhiteSpace(entity.ParentId))
        {
            entity = level.Entities.Find(e => e.Id == entity.ParentId) ?? throw new InvalidDataException("Missing bake parent.");
            if (!seen.Add(entity.Id)) throw new InvalidDataException("Cyclic bake hierarchy.");
            if (entity.CanPickUp || entity.Damageable || entity.IsKinematic || entity.MotionType != MotionType.Static || entity.Interaction != null || entity.Scripts.Count > 0)
                throw new InvalidDataException($"{entity.Name}: a bake participant cannot have a movable/interactable parent.");
        }
    }

    public static Matrix4x4 BoundsFit(LoadedModel model, Vector3 position, Vector3 size, Quaternion rotation)
    {
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        foreach (var part in model.Parts) foreach (var vertex in part.Positions) { min = Vector3.Min(min, vertex); max = Vector3.Max(max, vertex); }
        return Matrix4x4.CreateTranslation(-(min+max)*.5f) * Matrix4x4.CreateScale(Vector3.Abs(size)/Vector3.Max(max-min, new(.0001f))) *
            Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
    }
    private static int Closest(Ray ray, float max, MeshCollisionMesh mesh, List<int> candidates, out float distance)
    {
        mesh.QueryRay(ray, Bias, max, candidates); int hit = -1; distance = max;
        foreach (int index in candidates)
        {
            var tri = mesh.Triangles[index];
            if (Raycast.RayIntersectsTriangle(ray, tri.A, tri.B, tri.C, Bias, distance, out float t)) { hit = index; distance = t; }
        }
        return hit;
    }
    private static Vector3 Barycentric(Vector3 p, MeshCollisionTriangle t)
    {
        var v0=t.B-t.A; var v1=t.C-t.A; var v2=p-t.A;
        float a=Vector3.Dot(v0,v0), b=Vector3.Dot(v0,v1), c=Vector3.Dot(v1,v1), d=Vector3.Dot(v2,v0), e=Vector3.Dot(v2,v1);
        float denominator=a*c-b*b;
        if (MathF.Abs(denominator) < 1e-12f) return new(1,0,0);
        float v=(c*d-b*e)/denominator, w=(a*e-b*d)/denominator; return new(1-v-w,v,w);
    }
    private static float RadicalInverse(uint bits)
    {
        bits=(bits<<16)|(bits>>16); bits=((bits&0x55555555)<<1)|((bits&0xAAAAAAAA)>>1);
        bits=((bits&0x33333333)<<2)|((bits&0xCCCCCCCC)>>2); bits=((bits&0x0F0F0F0F)<<4)|((bits&0xF0F0F0F0)>>4);
        bits=((bits&0x00FF00FF)<<8)|((bits&0xFF00FF00)>>8); return bits*2.3283064365386963e-10f;
    }
    private static float Linear(float value) => value <= .04045f ? value/12.92f : MathF.Pow((value+.055f)/1.055f, 2.4f);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static Albedo ReadAlbedo(LoadedModelPart part, bool pbr = false)
    {
        var result = new Albedo { Factor = Vector3.Clamp(new(part.Color.X,part.Color.Y,part.Color.Z), Vector3.Zero, Vector3.One), Metallic = Math.Clamp(part.MetallicFactor,0,1) };
        if ((!pbr || part.TexCoords != null) && part.BaseColorPng != null) result.Rgba = Pixels(part.BaseColorPng, TextureSemantic.Color, out result.Width, out result.Height);
        if ((!pbr || part.TexCoords != null) && part.MetallicRoughnessPng != null) result.MaterialRgba = Pixels(part.MetallicRoughnessPng, TextureSemantic.Material, out result.MaterialWidth, out result.MaterialHeight);
        return result;
    }
    private static byte[] Pixels(byte[] image, TextureSemantic semantic, out int width, out int height)
    {
        var texture = TextureCooker.PrepareImage(image, semantic, null, false);
        int mip = Math.Min(texture.Mips.Length-1, Math.Max(0,(int)MathF.Log2(Math.Max(texture.Width,texture.Height)/128f)));
        width = Math.Max(1, texture.Width>>mip); height = Math.Max(1, texture.Height>>mip);
        return texture.Compressed ? MemoryMarshal.AsBytes(new BcDecoder().DecodeRaw(texture.Mips[mip],width,height,CompressionFormat.Bc7).AsSpan()).ToArray() : texture.Mips[mip];
    }
}
