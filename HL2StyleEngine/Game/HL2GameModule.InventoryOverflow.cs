using System.Numerics;
using Game.Inventory;
using Engine.Runtime.Entities;

namespace Game;

public sealed partial class HL2GameModule
{
    private sealed class DroppedItemSaveData
    {
        public string ItemId { get; set; } = "";
        public int Count { get; set; }
        public string Name { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    private List<DroppedItemSaveData> CaptureDroppedItems()
    {
        var result = new List<DroppedItemSaveData>();
        foreach (var entity in _runtimeEntities)
        {
            if (entity.Render.Shape == RuntimeShapeKind.None || !entity.Name.Contains("__Spawn", StringComparison.Ordinal) ||
                !TryGetWorldItem(entity, out string itemId, out int count)) continue;
            Vector3 position = entity.Transform.Position;
            result.Add(new DroppedItemSaveData { ItemId = itemId, Count = count, Name = entity.Name,
                X = position.X, Y = position.Y, Z = position.Z });
        }
        return result;
    }

    private void RestoreDroppedItems(List<DroppedItemSaveData> items)
    {
        _runtimeEntities.RemoveAll(entity => entity.Name.Contains("__Spawn", StringComparison.Ordinal));
        foreach (var item in items)
        {
            if (item.Count <= 0 || string.IsNullOrWhiteSpace(item.ItemId)) continue;
            Entity entity = SpawnWorldItemAt(item.ItemId, item.Count, new Vector3(item.X, item.Y, item.Z), Quaternion.Identity);
            if (!string.IsNullOrWhiteSpace(item.Name)) entity.Name = item.Name;
        }
    }

    private bool ReturnOverflowToWorld()
    {
        CancelInventoryMove();
        CloseInventoryActionUi();
        int index = 0;
        foreach (InventoryItemStack stack in _inventory.OverflowStacks.ToArray())
        {
            try
            {
                // Keep drops by the player, not two metres through a nearby wall.
                Vector3 position = _motor.Position + new Vector3(
                    (index % 4 - 1.5f) * 0.14f, 0.45f + index / 4 * 0.05f, 0f);
                SpawnWorldItemAt(stack.ItemId, stack.Count, position, Quaternion.Identity);
                _inventory.RemoveStackAtSlot(stack.SlotIndex, out _);
                index++;
            }
            catch (Exception ex)
            {
                ShowGameMessage($"Could not drop overflow: {ex.Message}");
                return false;
            }
        }
        return true;
    }
}
