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

    public InventoryItemStack? GetStackAtSlot(int slotIndex)
        => _stacks.FirstOrDefault(stack => stack.SlotIndex == slotIndex);

    public InventoryItemStack? GetStackCoveringSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCapacity)
            return null;

        foreach (InventoryItemStack stack in _stacks)
        {
            if (FootprintContainsSlot(stack.SlotIndex, ItemCatalog.Get(stack.ItemId), slotIndex))
                return stack;
        }

        return null;
    }

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

    public bool CanMoveStackToSlot(int fromSlotIndex, int toSlotIndex)
    {
        InventoryItemStack? stack = GetStackAtSlot(fromSlotIndex);
        if (stack == null)
            return false;

        return CanPlaceAt(ItemCatalog.Get(stack.ItemId), toSlotIndex, stack);
    }

    public bool CanMoveOrSwapStackToSlot(int fromSlotIndex, int toSlotIndex)
    {
        InventoryItemStack? moving = GetStackAtSlot(fromSlotIndex);
        if (moving == null)
            return false;

        InventoryItemStack? target = GetStackCoveringSlot(toSlotIndex);
        if (target == null || ReferenceEquals(target, moving))
            return CanPlaceAt(ItemCatalog.Get(moving.ItemId), toSlotIndex, moving);

        return CanSwap(moving, toSlotIndex, target);
    }

    public bool MoveStackToSlot(int fromSlotIndex, int toSlotIndex)
    {
        InventoryItemStack? stack = GetStackAtSlot(fromSlotIndex);
        if (stack == null)
            return false;

        if (!CanPlaceAt(ItemCatalog.Get(stack.ItemId), toSlotIndex, stack))
            return false;

        stack.MoveToSlot(toSlotIndex);
        return true;
    }

    public bool MoveOrSwapStackToSlot(int fromSlotIndex, int toSlotIndex, out bool swapped)
    {
        swapped = false;
        InventoryItemStack? moving = GetStackAtSlot(fromSlotIndex);
        if (moving == null)
            return false;

        InventoryItemStack? target = GetStackCoveringSlot(toSlotIndex);
        if (target == null || ReferenceEquals(target, moving))
            return MoveStackToSlot(fromSlotIndex, toSlotIndex);

        if (!CanSwap(moving, toSlotIndex, target))
            return false;

        int originalSlot = moving.SlotIndex;
        moving.MoveToSlot(toSlotIndex);
        target.MoveToSlot(originalSlot);
        swapped = true;
        return true;
    }

    public IEnumerable<int> CoveredSlots(InventoryItemStack stack)
        => CoveredSlots(stack.SlotIndex, ItemCatalog.Get(stack.ItemId));

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

    private bool CanSwap(InventoryItemStack moving, int movingTargetSlot, InventoryItemStack target)
    {
        InventoryItemDefinition movingDefinition = ItemCatalog.Get(moving.ItemId);
        InventoryItemDefinition targetDefinition = ItemCatalog.Get(target.ItemId);
        int movingOriginalSlot = moving.SlotIndex;
        int targetOriginalSlot = target.SlotIndex;

        return CanPlaceAt(movingDefinition, movingTargetSlot, moving, target) &&
               CanPlaceAt(targetDefinition, movingOriginalSlot, target, moving) &&
               !FootprintsOverlap(movingTargetSlot, movingDefinition, movingOriginalSlot, targetDefinition);
    }

    private bool CanPlaceAt(
        InventoryItemDefinition definition,
        int slotIndex,
        InventoryItemStack? ignoredA,
        InventoryItemStack? ignoredB)
    {
        if (slotIndex < 0 || slotIndex >= SlotCapacity)
            return false;

        int startX = slotIndex % GridWidth;
        int startY = slotIndex / GridWidth;
        if (startX + definition.SlotWidth > GridWidth || startY + definition.SlotHeight > GridHeight)
            return false;

        foreach (InventoryItemStack stack in _stacks)
        {
            if (ReferenceEquals(stack, ignoredA) || ReferenceEquals(stack, ignoredB))
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

    private bool FootprintContainsSlot(int originSlot, InventoryItemDefinition definition, int slotIndex)
        => CoveredSlots(originSlot, definition).Contains(slotIndex);

    private IEnumerable<int> CoveredSlots(int originSlot, InventoryItemDefinition definition)
    {
        int originX = originSlot % GridWidth;
        int originY = originSlot / GridWidth;

        for (int y = 0; y < definition.SlotHeight; y++)
        {
            for (int x = 0; x < definition.SlotWidth; x++)
            {
                int slotX = originX + x;
                int slotY = originY + y;
                if (slotX >= 0 && slotX < GridWidth && slotY >= 0 && slotY < GridHeight)
                    yield return slotY * GridWidth + slotX;
            }
        }
    }

    private void Restore(List<InventoryItemStack> snapshot)
    {
        _stacks.Clear();
        _stacks.AddRange(snapshot);
    }
}
