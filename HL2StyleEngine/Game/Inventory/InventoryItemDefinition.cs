namespace Game.Inventory;

public sealed class InventoryItemDefinition
{
    public InventoryItemDefinition(
        string id,
        string displayName,
        string description,
        InventoryItemType type,
        int slotWidth,
        int slotHeight,
        int maxStack,
        bool expiresWhenMatchingLocksOpened = false)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Type = type;
        SlotWidth = Math.Max(1, slotWidth);
        SlotHeight = Math.Max(1, slotHeight);
        MaxStack = Math.Max(1, maxStack);
        ExpiresWhenMatchingLocksOpened = expiresWhenMatchingLocksOpened;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public InventoryItemType Type { get; }
    public int SlotWidth { get; }
    public int SlotHeight { get; }
    public int SlotCount => SlotWidth * SlotHeight;
    public int MaxStack { get; }
    public bool ExpiresWhenMatchingLocksOpened { get; }
}
