namespace Game.Inventory;

public sealed class InventoryItemStack
{
    public InventoryItemStack(string itemId, int count)
    {
        ItemId = itemId;
        Count = Math.Max(1, count);
    }

    public string ItemId { get; }
    public int Count { get; private set; }

    public void Add(int count)
        => Count = Math.Max(1, Count + count);

    public void Remove(int count)
        => Count = Math.Max(0, Count - count);
}
