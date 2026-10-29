using Game.Inventory;

namespace Game;

public sealed partial class HL2GameModule
{
    private readonly Queue<string> _pendingUiImages = new();
    private const long UiPrewarmBudget = 16 * 1024 * 1024;

    private void QueueLevelUiImages()
    {
        _pendingUiImages.Clear();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in _inventory.Stacks.Concat(_storage.Stacks)) ids.Add(stack.ItemId);
        foreach (var entity in _runtimeEntities)
            if (TryGetWorldItem(entity, out string id, out _)) ids.Add(id);
        // Current/save/level items get first admission. Registered UI thumbnails are a
        // bounded lower-priority tail, covering crafted/reward items not spawned yet.
        foreach (string path in ids.Concat(ItemCatalog.Definitions.Select(d => d.Id))
                     .Where(id => ItemCatalog.Get(id).Type is not (InventoryItemType.Weapon or InventoryItemType.Ammo))
                     .Select(GetInventoryIconPath).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(64))
            _pendingUiImages.Enqueue(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Content", "UI", "Runtime", path)));
    }

    private void PrewarmNextUiImage()
    {
        // Use existing loading frames only; never delay input readiness or alter inventory state.
        if (_editorEnabled || _loadingOverlayTimer <= 0 || !_pendingUiImages.TryDequeue(out string? path)) return;
        using var measure = _ctx.Loading?.Measure("startup-ui-image-prewarm");
        if (_ctx.ImGui?.TryPreloadImage(path, UiPrewarmBudget) == true)
        {
            var image = _ctx.ImGui.GetImage(path);
            _gameplayUi?.QueuePreloadIcon(path, (long)image.Width * image.Height * 4);
        }
    }
}
