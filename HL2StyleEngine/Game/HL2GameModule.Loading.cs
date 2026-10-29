using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Hosting;
using Game.Inventory;
using Game.Weapons;
using Engine.Core.Assets;

namespace Game;

public sealed partial class HL2GameModule : ILoadReadiness
{
    private readonly string _assetRoot;
    private readonly AssetPackage? _assetPackage;
    private int _readyFrames;
    private AssetFingerprintReader? _strictFingerprints, _packageFingerprints;
    public bool PackageLightingEnabled { get; set; } = true;
    private AssetFingerprintReader? LightingFingerprints => !_editorEnabled && PackageLightingEnabled ? _packageFingerprints : _strictFingerprints;

    private void InitializeFingerprintReaders()
    {
        _strictFingerprints = _ctx.Loading == null ? null : new(_ctx.Loading);
        _packageFingerprints = _assetPackage == null ? _strictFingerprints : new(_ctx.Loading, _assetPackage.TryGetModelIdentity);
    }

    public LoadReadiness GetLoadReadiness()
    {
        int failed = _requiredModels.Count(p => _weaponModelCache.TryGetValue(p, out var e) && e.Failed);
        bool assets = _requiredModels.Count > 0 && _requiredModels.All(p => _weaponModelCache.TryGetValue(p, out var e) && e.Model != null);
        var level = _editor.LevelFile;
        bool lighting = (level.Bake?.Enabled != true || (_bakedLighting.Ready && !_bakedLighting.Busy && _bakedLighting.BindingsReady && _bakedLighting.LastBindingIssue.Length == 0)) &&
            ((!level.AutomaticRoomReflections && string.IsNullOrEmpty(level.ReflectionProbeId)) || (_roomReflections.Ready && !_roomReflections.Busy)) &&
            (level.ReflectionEnvironment == ReflectionPreset.None || _world.ReflectionReady);
        bool input = !_editorEnabled && !GameplayModalOpen && !_streamingBlocked && _loadingOverlayTimer <= 0;
        // One settled follow-up render includes bindings requested by newly uploaded models.
        _readyFrames = assets && lighting && input && failed == 0 ? _readyFrames + 1 : 0;
        return new(assets, lighting, input && _readyFrames >= 2, failed, _editor.LevelPath,
            $"{_bakedLighting.Status}; {_roomReflections.Status}", new
            {
                Mode = _assetPackage == null ? "strict-source" : "immutable-package",
                LightingValidation = _assetPackage != null && PackageLightingEnabled && !_editorEnabled ? "package-model-identities" : "strict-source",
                LightingIdentityHits = _packageFingerprints?.IdentityHits ?? 0,
                PackageHits = _assetPackage?.Hits ?? 0, PackageFallbacks = _assetPackage?.Fallbacks ?? 0,
                RequiredModels = _requiredModels.Count, ResidentModels = StreamingResidentModels,
                ModelBytes = StreamingResidentBytes, CollisionPreparationMilliseconds, CollisionCacheHits, CollisionMeshesBuilt,
                Failed = _requiredModels.Where(p => _weaponModelCache.TryGetValue(p, out var e) && e.Failed)
                    .Select(p => new { Path = p, _weaponModelCache[p].Error }).ToArray()
            });
    }

    public static IEnumerable<string> PackageModelDependencies(LevelFile level)
    {
        var paths = level.Entities.SelectMany(e => new[] { e.MeshPath ?? "" }
            .Concat(e.BreakReplacementModelPaths ?? []).Concat(e.BreakDebrisModelPaths ?? []));
        paths = paths.Concat(WeaponDefinitions.All.Select(w => w.ViewModel.ModelAssetPath ?? ""))
            .Append(PlayerCharacterModelPath)
            .Concat(new[] { ItemCatalog.HealthPack, ItemCatalog.SuitBattery, ItemCatalog.DamagedCable,
                ItemCatalog.RepairedCable, ItemCatalog.SpareWire, ItemCatalog.MaintenanceKey, ItemCatalog.MasterKey }
                .Select(GetSpawnedItemModelPath));
        return paths.Where(IsGlbModelPath).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase);
    }
}
