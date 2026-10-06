using Engine.Runtime.Entities;

namespace Game;

public sealed partial class HL2GameModule
{
    private readonly Dictionary<Entity, (string Path, bool Persist, bool Message)> _pendingReplacements = new();

    private void PumpModelUploads()
    {
        _streamingBlocked = false;
        foreach (string path in _requiredModels)
            if (!TryGetReadyModel(path, "model", out var entry)) _streamingBlocked |= entry is { Failed: false };
        foreach (string path in _wantedModels)
            if (!_requiredModels.Contains(path)) TryGetReadyModel(path, "prefetch", out _);
        if (_streamingBlocked) _loadingOverlayTimer = MathF.Max(_loadingOverlayTimer, .15f);
    }

    private void UpdatePendingReplacements()
    {
        if (_pendingReplacements.Count == 0) return;
        foreach (var pair in _pendingReplacements.ToArray())
        {
            if (!_runtimeEntities.Contains(pair.Key)) { _pendingReplacements.Remove(pair.Key); continue; }
            string path = ResolveModelAssetPath(pair.Value.Path);
            if (_weaponModelCache.TryGetValue(path, out var entry) && entry.Failed)
            {
                _pendingReplacements.Remove(pair.Key);
                if (pair.Value.Message) ShowGameMessage($"Replacement failed: {entry.Error}. Original object retained.", 3f);
                continue;
            }
            if (entry?.Model == null) continue;
            _pendingReplacements.Remove(pair.Key);
            BreakDamageableEntity(pair.Key, pair.Value.Path, pair.Value.Persist, pair.Value.Message);
        }
    }
}
