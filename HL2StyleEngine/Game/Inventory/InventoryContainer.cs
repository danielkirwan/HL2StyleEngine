namespace Game.Inventory;

public sealed class InventoryContainer
{
    private readonly List<InventoryItemStack> _stacks = new();

    public InventoryContainer(int gridWidth, int gridHeight)
    {
        GridWidth = Math.Max(1, gridWidth);
        GridHeight = Math.Max(1, gridHeight);
    }

    public int GridWidth { get; }
    public int GridHeight { get; }
    public int SlotCapacity => GridWidth * GridHeight;
    public int UsedSlotCount => _stacks.Sum(stack => ItemCatalog.Get(stack.ItemId).SlotCount);
    public int StackCount => _stacks.Count;
    public bool IsEmpty => _stacks.Count == 0;
    public IReadOnlyList<InventoryItemStack> Stacks => _stacks;

    public bool Contains(string itemId)
        => _stacks.Any(stack => string.Equals(stack.ItemId, itemId, StringComparison.OrdinalIgnoreCase));

    public int GetCount(string itemId)
        => _stacks
            .Where(stack => string.Equals(stack.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
            .Sum(stack => stack.Count);

    public bool Add(string itemId, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            return false;

        List<InventoryItemStack> snapshot = _stacks
            .Select(stack => new InventoryItemStack(stack.ItemId, stack.Count, stack.SlotIndex))
            .ToList();

        InventoryItemDefinition definition = ItemCatalog.Get(itemId);
        if (definition.MaxStack == 1 && Contains(itemId))
            return true;

        int remaining = count;
        foreach (InventoryItemStack stack in _stacks)
        {
            if (!string.Equals(stack.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                continue;

            int room = definition.MaxStack - stack.Count;
            if (room <= 0)
                continue;

            int added = Math.Min(room, remaining);
            stack.Add(added);
            remaining -= added;
            if (remaining <= 0)
                return true;
        }

        while (remaining > 0)
        {
            int stackCount = Math.Min(definition.MaxStack, remaining);
            int slotIndex = FindFirstFreeSlot(definition);
            if (slotIndex < 0)
            {
                Restore(snapshot);
                return false;
            }

            _stacks.Add(new InventoryItemStack(itemId, stackCount, slotIndex));
            remaining -= stackCount;
        }

        return true;
    }

    public bool RemoveStack(string itemId)
    {
        int removed = _stacks.RemoveAll(stack => string.Equals(stack.ItemId, itemId, StringComparison.OrdinalIgnoreCase));
        return removed > 0;
    }

    public bool RemoveCount(string itemId, int count = 1)
    {
        if (count <= 0)
            return true;

        int available = GetCount(itemId);
        if (available < count)
            return false;

        int remaining = count;
        for (int i = _stacks.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventoryItemStack stack = _stacks[i];
            if (!string.Equals(stack.ItemId, itemId, StringComparison.OrdinalIgnoreCase))
                continue;

            int removed = Math.Min(stack.Count, remaining);
            stack.Remove(removed);
            remaining -= removed;

            if (stack.Count <= 0)
                _stacks.RemoveAt(i);
        }

        return true;
    }

    public void Clear()
        => _stacks.Clear();

    public IEnumerable<string> ItemIds()
        => _stacks.Select(stack => stack.ItemId).Distinct(StringComparer.OrdinalIgnoreCase);

    public List<InventoryItemSaveData> ToSaveData()
        => _stacks
            .Select(stack => new InventoryItemSaveData { ItemId = stack.ItemId, Count = stack.Count, SlotIndex = stack.SlotIndex })
            .ToList();

    public void LoadFromSave(IEnumerable<InventoryItemSaveData> items)
    {
        Clear();
        foreach (InventoryItemSaveData item in items.OrderBy(item => item.SlotIndex < 0 ? int.MaxValue : item.SlotIndex))
        {
            if (item.SlotIndex >= 0 && TryAddStackAt(item.ItemId, item.Count, item.SlotIndex))
                continue;

            Add(item.ItemId, item.Count);
        }
    }

    private bool TryAddStackAt(string itemId, int count, int slotIndex)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            return false;

        InventoryItemDefinition definition = ItemCatalog.Get(itemId);
        if (count > definition.MaxStack)
            return false;

        if (!CanPlaceAt(definition, slotIndex, ignoredStack: null))
            return false;

        _stacks.Add(new InventoryItemStack(itemId, count, slotIndex));
        return true;
    }

    private int FindFirstFreeSlot(InventoryItemDefinition definition)
    {
        for (int slot = 0; slot < SlotCapacity; slot++)
        {
            if (CanPlaceAt(definition, slot, ignoredStack: null))
                return slot;
        }

        return -1;
    }

    private bool CanPlaceAt(InventoryItemDefinition definition, int slotIndex, InventoryItemStack? ignoredStack)
    {
        if (slotIndex < 0 || slotIndex >= SlotCapacity)
            return false;

        int startX = slotIndex % GridWidth;
        int startY = slotIndex / GridWidth;
        if (startX + definition.SlotWidth > GridWidth || startY + definition.SlotHeight > GridHeight)
            return false;

        foreach (InventoryItemStack stack in _stacks)
        {
            if (ReferenceEquals(stack, ignoredStack))
                continue;

            if (FootprintsOverlap(slotIndex, definition, stack.SlotIndex, ItemCatalog.Get(stack.ItemId)))
                return false;
        }

        return true;
    }

    private bool FootprintsOverlap(
        int aSlot,
        InventoryItemDefinition a,
        int bSlot,
        InventoryItemDefinition b)
    {
        int ax = aSlot % GridWidth;
        int ay = aSlot / GridWidth;
        int bx = bSlot % GridWidth;
        int by = bSlot / GridWidth;

        return ax < bx + b.SlotWidth &&
               ax + a.SlotWidth > bx &&
               ay < by + b.SlotHeight &&
               ay + a.SlotHeight > by;
    }

    private void Restore(List<InventoryItemStack> snapshot)
    {
        _stacks.Clear();
        _stacks.AddRange(snapshot);
    }
}
