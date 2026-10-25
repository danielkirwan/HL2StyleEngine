using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Engine.Core.Serialization;
using Engine.Editor.Level;
using Engine.Physics.Dynamics;
using Engine.Render;

namespace Engine.Editor.Editor;

public sealed class ReflectionSnapshot
{
    public required LevelFile Level { get; init; }
    public required string LevelPath { get; init; }
    public required LevelEntityDef Probe { get; init; }
    public required Vector3 Position { get; init; }
    public required byte[] Json { get; init; }
    public required Dictionary<string,string> ModelPaths { get; init; }
    public required Dictionary<string,string> Dependencies { get; init; }
    public required string[] FixedLightIds { get; init; }
    public BakeSnapshot? DiffuseSnapshot { get; init; }
    public ReflectionProbeSettings Settings => Probe.ReflectionProbe!;

    public static ReflectionSnapshot Create(LevelFile source, string levelPath, string probeId, Func<string,string> resolve)
    {
        return CreateOwned(StructuredData.ReadJson<LevelFile>(StructuredData.WriteJson(source)), levelPath, probeId, resolve);
    }

    internal static ReflectionSnapshot CreateOwned(LevelFile level, string levelPath, string probeId, Func<string,string> resolve)
    {
        var probe = level.Entities.SingleOrDefault(e => e.Id == probeId && e.Type == EntityTypes.ReflectionProbe)
            ?? throw new InvalidDataException("Select a reflection probe.");
        var settings = probe.ReflectionProbe ?? throw new InvalidDataException("Probe settings missing.");
        if (level.LightingModel != SurfaceLightingModel.Pbr || settings.Resolution is not (32 or 64 or 128) ||
            settings.LightingPreset != ReflectionLightPreset.InvariantOnly || (settings.CaptureMask & ~ReflectionCaptureMask.All) != 0 ||
            !settings.CaptureMask.HasFlag(ReflectionCaptureMask.FixedGeometry))
            throw new InvalidDataException("Room captures require PBR, fixed geometry and a 32/64/128 invariant-light probe.");
        if (level.Entities.Any(e => string.IsNullOrWhiteSpace(e.Id)) || level.Entities.Select(e => e.Id).Distinct().Count() != level.Entities.Count)
            throw new InvalidDataException("Capture entities need unique stable IDs.");
        var editor = new LevelEditorController(); editor.LoadFromMemory(levelPath, level);
        int index = level.Entities.IndexOf(probe);
        if (!editor.TryGetEntityWorldTRS(index, out var position, out var rotation, out var scale) || !Finite(position) ||
            !Finite(scale) || Vector3.Min(scale, Vector3.Zero) != Vector3.Zero || scale.X * scale.Y * scale.Z <= 0 ||
            MathF.Abs(Quaternion.Dot(rotation, Quaternion.Identity)) < .99999f || !Invariant(probe, level))
            throw new InvalidDataException("Probe must have a fixed, positive-scale, axis-aligned parent chain.");
        foreach (Vector3 size in new Vector3[] { settings.InfluenceSize, settings.ProjectionSize })
            if (!Finite(size) || size.X <= 0 || size.Y <= 0 || size.Z <= 0 || MathF.Max(size.X, MathF.Max(size.Y, size.Z)) > 500)
                throw new InvalidDataException("Probe bounds must be positive and at most 500 metres.");
        var diffuse = level.Bake.Enabled && settings.CaptureMask.HasFlag(ReflectionCaptureMask.BakedDiffuse)
            ? BakeSnapshot.Capture(level, resolve) : null;
        // Clear preview-only choices; exposure/UI never enter the HDR capture.
        level.ReflectionProbeId = null; level.ReflectionEnvironment = ReflectionPreset.None; level.Exposure = 1;
        level.AutomaticRoomReflections = false;
        level.ReflectionStreaming = null;
        // Sampling links do not contribute energy to the capture.
        foreach (var entity in level.Entities)
            if (entity.ReflectionProbe != null) entity.ReflectionProbe.Doorway = null;
        var controlled = level.Entities.Where(e => e.Interaction != null)
            .SelectMany(e => LevelLightState.Targets(level, e.Interaction!)).Select(e => e.Id).ToHashSet();
        var fixedLights = level.Entities.Where(e => e.Type == EntityTypes.PointLight && e.BakeIndirect &&
            e.FlickerAmount == 0 && !controlled.Contains(e.Id) && Invariant(e, level)).ToArray();
        var fixedIds = fixedLights.Where(e => e.LightEnabled && level.UsePointLights).Select(e => e.Id).ToArray();
        foreach (var light in level.Entities.Where(e => e.Type == EntityTypes.PointLight))
            if (!fixedLights.Contains(light)) light.LightEnabled = false;
        var geometry = level.Entities.Where(IndirectLightBaker.IsStaticGeometry).ToArray();
        if (geometry.Length is < 1 or > 1024 || geometry.Any(e => !Invariant(e, level)))
            throw new InvalidDataException("Capture requires 1-1024 eligible Static Lighting models with fixed parent chains.");
        foreach (var entity in geometry)
        {
            if (!settings.CaptureMask.HasFlag(ReflectionCaptureMask.InvariantEmission)) entity.EmissionIntensity = 0;
            // Linked emission is invariant only when its enabling lamp is part of the fixed preset.
            if (!string.IsNullOrWhiteSpace(entity.EmissionLightId) && !fixedIds.Contains(entity.EmissionLightId)) entity.EmissionIntensity = 0;
        }
        var paths = geometry.Select(e => e.MeshPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(p => p, resolve, StringComparer.OrdinalIgnoreCase);
        var dependencies = new Dictionary<string,string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (string shader in new[] { "TexturedModelVS.cso", "TexturedModelPS.cso", "ShadowVS.cso" })
            dependencies["shader/" + shader] = Path.Combine(AppContext.BaseDirectory, "Shaders", shader);
        if (diffuse != null) dependencies["diffuse"] = LightmapFile.PathFor(levelPath);
        return new() { Level = level, LevelPath = levelPath, Probe = probe, Position = position,
            Json = StructuredData.WriteJson(level), ModelPaths = paths, Dependencies = dependencies,
            FixedLightIds = fixedIds, DiffuseSnapshot = diffuse };
    }

    public static bool Invariant(LevelEntityDef entity, LevelFile level)
    {
        var seen = new HashSet<string>();
        for (var current = entity; current != null;)
        {
            if (!seen.Add(current.Id) || current.MotionType != MotionType.Static || current.IsKinematic || current.CanPickUp ||
                current.Damageable || current.Scripts.Count != 0 || current.Interaction != null) return false;
            if (string.IsNullOrWhiteSpace(current.ParentId)) return true;
            current = level.Entities.Find(e => e.Id == current.ParentId);
            if (current == null) return false;
        }
        return false;
    }

    public string Key(CancellationToken cancel = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("HS2-room-capture-1/filter-" + ReflectionEnvironmentData.Version)); hash.AppendData(Json);
        foreach (var entry in Dependencies.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            cancel.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Key));
            using var stream = File.OpenRead(entry.Value); hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public string Stamp()
    {
        try { return string.Join('|', Dependencies.Values.Select(p => { var f = new FileInfo(p); return $"{f.Length}:{f.LastWriteTimeUtc.Ticks}"; })); }
        catch (IOException) { return "missing"; }
        catch (UnauthorizedAccessException) { return "unavailable"; }
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
