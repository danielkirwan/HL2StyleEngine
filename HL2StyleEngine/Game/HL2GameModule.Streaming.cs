using System.Diagnostics;
using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Entities;
using Game.Streaming;

namespace Game;

public sealed partial class HL2GameModule
{
    private readonly Stopwatch _assetClock = Stopwatch.StartNew();
    private static readonly SemaphoreSlim ModelLoadWorkers = new(2);
    private readonly HashSet<string> _residentPins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _wantedModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _requiredModels = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _evictionCandidates = new();
    private readonly Dictionary<string, string> _resolvedModelPaths = new(StringComparer.OrdinalIgnoreCase);
    private RoomAssetManifest? _roomAssets;
    private bool _streamingBlocked;
    public bool AssetStreamingEnabled { get; set; } = true;
    public int StreamingEvictions { get; private set; }
    public int StreamingZoneCount => _roomAssets?.Zones.Count ?? 0;
    public int StreamingWantedModels => _wantedModels.Count;
    public int StreamingResidentModels => _weaponModelCache.Values.Count(e => e.Model != null);
    public long StreamingResidentBytes => _world.TextureResidentBytes + _weaponModelCache.Values.Sum(e => e.Model?.GeometryBytes ?? 0);
    public bool StreamingOverBudget => StreamingResidentBytes > Math.Clamp((_editor.LevelFile.Streaming ?? new()).BudgetMiB, 1, 4096) * 1048576L;
    public bool StreamingWaiting => _streamingBlocked;
    public RoomAssetManifest? StreamingManifest => _roomAssets;

    private static void StartModelLoad(string path, WeaponModelCacheEntry entry)
    {
        if (entry.Failed || entry.LoadedModel != null || entry.LoadTask != null) return;
        entry.LoadTask = Task.Run(async () =>
        {
            await ModelLoadWorkers.WaitAsync();
            try { return TextureCooker.LoadForRendering(path); }
            finally { ModelLoadWorkers.Release(); }
        });
    }

    private void PlanModelResidency()
    {
        var settings = _editor.LevelFile.Streaming ?? new LevelStreamingSettings();
        float zoneSize = float.IsFinite(settings.ZoneSize) ? Math.Clamp(settings.ZoneSize, 8, 128) : 32;
        if (_roomAssets == null || _roomAssets.ZoneSize != zoneSize)
            _roomAssets = new RoomAssetManifest(Placements(), zoneSize);
        _wantedModels.Clear(); _requiredModels.Clear();
        _requiredModels.UnionWith(_residentPins);
        bool stream = AssetStreamingEnabled && settings.Enabled && !_editorEnabled;
        if (stream) _roomAssets.Gather(_camera.Position, settings.PreloadNeighbours, _wantedModels);
        else _wantedModels.UnionWith(_weaponModelCache.Keys);

        foreach (var entity in _runtimeEntities)
        {
            if (!entity.Render.Enabled) continue;
            RenderBounds(entity, out var min, out var max);
            bool near = Vector3.DistanceSquared(Vector3.Clamp(_camera.Position, min, max), _camera.Position) < zoneSize * zoneSize;
            bool needed = !stream || entity.IsHeld || near || _world.IntersectsView(min, max, Matrix4x4.Identity) ||
                _world.IsShadowRelevant((min + max) * .5f, (max - min).Length() * .5f);
            if (!needed) continue;
            AddPath(entity.Render.ModelAssetPath, _requiredModels);
            foreach (string path in entity.BreakReplacementModelPaths) AddPath(path, _wantedModels);
            foreach (string path in entity.BreakDebrisModelPaths) AddPath(path, _wantedModels);
        }
        // Editor authoring, in-flight replacements and live fragments are independent owners.
        if (_editorEnabled)
            foreach (var entity in _editor.LevelFile.Entities) AddPath(entity.MeshPath, _requiredModels);
        foreach (var pending in _pendingReplacements)
        {
            AddPath(pending.Key.Render.ModelAssetPath, _requiredModels);
            AddPath(pending.Value.Path, _wantedModels);
        }
        foreach (var piece in _fractureDebrisPieces) AddPath(piece.ModelAssetPath, _requiredModels);
        if (_held != null) AddPath(_held.Render.ModelAssetPath, _requiredModels);
        _wantedModels.UnionWith(_requiredModels);
        // Critical uploads are issued before speculative neighbour prefetches.
        foreach (string path in _requiredModels) PreloadModelAsset(path);
        foreach (string path in _wantedModels) PreloadModelAsset(path);

        IEnumerable<AssetPlacement> Placements()
        {
            foreach (var entity in _runtimeEntities)
            {
                RenderBounds(entity, out var min, out var max);
                if (!string.IsNullOrWhiteSpace(entity.Render.ModelAssetPath))
                    yield return new(ResolveModelAssetPath(entity.Render.ModelAssetPath), min, max);
                foreach (string path in entity.BreakReplacementModelPaths.Concat(entity.BreakDebrisModelPaths))
                    if (!string.IsNullOrWhiteSpace(path)) yield return new(ResolveModelAssetPath(path), min, max);
            }
        }
    }

    private void AddPath(string? path, HashSet<string> target)
    { if (!string.IsNullOrWhiteSpace(path)) target.Add(ResolveModelAssetPath(path)); }

    private static void RenderBounds(Entity entity, out Vector3 min, out Vector3 max)
    {
        Vector3 size = Vector3.Abs(entity.Render.Size);
        if (entity.Render.Shape is RuntimeShapeKind.Sphere or RuntimeShapeKind.Capsule)
            size = Vector3.Max(size, new Vector3(entity.Render.Radius * 2, MathF.Max(entity.Render.Height, entity.Render.Radius * 2), entity.Render.Radius * 2));
        var m = Matrix4x4.CreateFromQuaternion(GetColliderRotation(entity));
        Vector3 e = size * .5f;
        Vector3 extent = new(MathF.Abs(m.M11) * e.X + MathF.Abs(m.M21) * e.Y + MathF.Abs(m.M31) * e.Z,
            MathF.Abs(m.M12) * e.X + MathF.Abs(m.M22) * e.Y + MathF.Abs(m.M32) * e.Z,
            MathF.Abs(m.M13) * e.X + MathF.Abs(m.M23) * e.Y + MathF.Abs(m.M33) * e.Z);
        min = entity.Transform.Position - extent; max = entity.Transform.Position + extent;
    }

    private void EvictDistantModels()
    {
        var settings = _editor.LevelFile.Streaming ?? new LevelStreamingSettings();
        if (!AssetStreamingEnabled || !settings.Enabled || _editorEnabled || _streamingBlocked) return;
        // Keep the previous working set until all incoming dependencies are GPU-ready (or failed).
        if (_wantedModels.Any(path => _weaponModelCache.TryGetValue(path, out var e) && !e.Failed && e.Model == null)) return;
        double grace = float.IsFinite(settings.RetainSeconds) ? Math.Clamp(settings.RetainSeconds, 0, 120) : 15;
        if (StreamingOverBudget) grace = Math.Min(grace, 2);
        _evictionCandidates.Clear();
        foreach (var pair in _weaponModelCache)
        {
            var entry = pair.Value;
            if (_wantedModels.Contains(pair.Key) || entry.Failed || entry.LoadTask is { IsCompleted: false } ||
                _assetClock.Elapsed.TotalSeconds - entry.LastWanted < grace) continue;
            _evictionCandidates.Add(pair.Key);
        }
        _evictionCandidates.Sort((a, b) => _weaponModelCache[a].LastWanted.CompareTo(_weaponModelCache[b].LastWanted));
        foreach (string path in _evictionCandidates.Take(2))
        {
            var entry = _weaponModelCache[path];
            if (entry.Model != null) _ctx.Renderer.RetireAfterFrame(entry.Model);
            if (entry.LoadedModel != null) _world.RetirePendingModel(entry.LoadedModel, _ctx.Renderer);
            _weaponModelCache.Remove(path);
            StreamingEvictions++;
        }
    }
}
