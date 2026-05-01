namespace Game.Inventory;

public sealed class InventoryItemSaveData
{
    public string ItemId { get; set; } = "";
    public int Count { get; set; }
    public int SlotIndex { get; set; } = -1;
    public bool Rotated { get; set; }
}
