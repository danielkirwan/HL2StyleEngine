namespace Game.Inventory;

public sealed class InventoryItemStack
{
    public InventoryItemStack(string itemId, int count, int slotIndex)
    {
        ItemId = itemId;
        Count = Math.Max(1, count);
        SlotIndex = Math.Max(0, slotIndex);
    }

    public string ItemId { get; }
    public int Count { get; private set; }
    public int SlotIndex { get; private set; }

    public void Add(int count)
        => Count = Math.Max(1, Count + count);

    public void Remove(int count)
        => Count = Math.Max(0, Count - count);

    public void MoveToSlot(int slotIndex)
        => SlotIndex = Math.Max(0, slotIndex);
}
