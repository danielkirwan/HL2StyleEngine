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
            _stacks.Add(new InventoryItemStack(itemId, stackCount));
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
            .Select(stack => new InventoryItemSaveData { ItemId = stack.ItemId, Count = stack.Count })
            .ToList();

    public void LoadFromSave(IEnumerable<InventoryItemSaveData> items)
    {
        Clear();
        foreach (InventoryItemSaveData item in items)
            Add(item.ItemId, item.Count);
    }
}
