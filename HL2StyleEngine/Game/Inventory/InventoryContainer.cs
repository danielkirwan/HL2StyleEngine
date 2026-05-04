namespace Game.Inventory;

public enum InventoryMoveResult
{
    None,
    Moved,
    Swapped,
    Merged,
    PartiallyMerged,
    StackFull
}

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
            if (FootprintContainsSlot(stack.SlotIndex, ItemCatalog.Get(stack.ItemId), stack.Rotated, slotIndex))
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
            .Select(stack => new InventoryItemStack(stack.ItemId, stack.Count, stack.SlotIndex, stack.Rotated))
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
            .Select(stack => new InventoryItemSaveData
            {
                ItemId = stack.ItemId,
                Count = stack.Count,
                SlotIndex = stack.SlotIndex,
                Rotated = stack.Rotated
            })
            .ToList();

    public void LoadFromSave(IEnumerable<InventoryItemSaveData> items)
    {
        Clear();
        foreach (InventoryItemSaveData item in items.OrderBy(item => item.SlotIndex < 0 ? int.MaxValue : item.SlotIndex))
        {
            if (item.SlotIndex >= 0 && TryAddStackAt(item.ItemId, item.Count, item.SlotIndex, item.Rotated))
                continue;

            Add(item.ItemId, item.Count);
        }
    }

    public bool CanMoveStackToSlot(int fromSlotIndex, int toSlotIndex)
    {
        InventoryItemStack? stack = GetStackAtSlot(fromSlotIndex);
        if (stack == null)
            return false;

        return CanPlaceAt(ItemCatalog.Get(stack.ItemId), stack.Rotated, toSlotIndex, stack);
    }

    public bool CanMoveOrSwapStackToSlot(int fromSlotIndex, int toSlotIndex)
        => CanMoveOrSwapStackToSlot(fromSlotIndex, toSlotIndex, GetStackAtSlot(fromSlotIndex)?.Rotated ?? false);

    public bool CanMoveOrSwapStackToSlot(int fromSlotIndex, int toSlotIndex, bool movingRotated)
    {
        InventoryItemStack? moving = GetStackAtSlot(fromSlotIndex);
        if (moving == null)
            return false;

        InventoryItemStack? target = GetStackCoveringSlot(toSlotIndex);
        if (target == null || ReferenceEquals(target, moving))
            return CanPlaceAt(ItemCatalog.Get(moving.ItemId), movingRotated, toSlotIndex, moving);

        if (CanMerge(moving, target))
            return true;

        return CanSwap(moving, movingRotated, toSlotIndex, target);
    }

    public bool MoveStackToSlot(int fromSlotIndex, int toSlotIndex)
    {
        InventoryItemStack? stack = GetStackAtSlot(fromSlotIndex);
        if (stack == null)
            return false;

        if (!CanPlaceAt(ItemCatalog.Get(stack.ItemId), stack.Rotated, toSlotIndex, stack))
            return false;

        stack.MoveToSlot(toSlotIndex);
        return true;
    }

    public bool MoveOrSwapStackToSlot(int fromSlotIndex, int toSlotIndex, out bool swapped)
    {
        bool moved = MoveOrMergeOrSwapStackToSlot(
            fromSlotIndex,
            toSlotIndex,
            GetStackAtSlot(fromSlotIndex)?.Rotated ?? false,
            out InventoryMoveResult result);
        swapped = result == InventoryMoveResult.Swapped;
        return moved;
    }

    public bool MoveOrSwapStackToSlot(int fromSlotIndex, int toSlotIndex, bool movingRotated, out bool swapped)
    {
        bool moved = MoveOrMergeOrSwapStackToSlot(fromSlotIndex, toSlotIndex, movingRotated, out InventoryMoveResult result);
        swapped = result == InventoryMoveResult.Swapped;
        return moved;
    }

    public bool MoveOrMergeOrSwapStackToSlot(
        int fromSlotIndex,
        int toSlotIndex,
        bool movingRotated,
        out InventoryMoveResult result)
    {
        result = InventoryMoveResult.None;
        InventoryItemStack? moving = GetStackAtSlot(fromSlotIndex);
        if (moving == null)
            return false;

        InventoryItemStack? target = GetStackCoveringSlot(toSlotIndex);
        if (target == null || ReferenceEquals(target, moving))
        {
            if (!CanPlaceAt(ItemCatalog.Get(moving.ItemId), movingRotated, toSlotIndex, moving))
                return false;

            moving.SetRotation(movingRotated);
            moving.MoveToSlot(toSlotIndex);
            result = InventoryMoveResult.Moved;
            return true;
        }

        if (TryMergeStacks(moving, target, out result))
            return true;

        if (!CanSwap(moving, movingRotated, toSlotIndex, target))
            return false;

        int originalSlot = moving.SlotIndex;
        moving.SetRotation(movingRotated);
        moving.MoveToSlot(toSlotIndex);
        target.MoveToSlot(originalSlot);
        result = InventoryMoveResult.Swapped;
        return true;
    }

    public bool CanSplitStackAtSlot(int slotIndex)
    {
        InventoryItemStack? stack = GetStackCoveringSlot(slotIndex);
        if (stack == null || stack.Count <= 1)
            return false;

        InventoryItemDefinition definition = ItemCatalog.Get(stack.ItemId);
        return definition.MaxStack > 1 && FindFirstFreeSlot(definition, stack.Rotated) >= 0;
    }

    public bool SplitStackAtSlot(int slotIndex, out int newSlotIndex, out int splitCount)
    {
        InventoryItemStack? stack = GetStackCoveringSlot(slotIndex);
        int amount = stack != null ? Math.Max(1, stack.Count / 2) : 1;
        return SplitStackAtSlot(slotIndex, amount, out newSlotIndex, out splitCount);
    }

    public bool SplitStackAtSlot(int slotIndex, int amount, out int newSlotIndex, out int splitCount)
    {
        newSlotIndex = -1;
        splitCount = 0;

        InventoryItemStack? stack = GetStackCoveringSlot(slotIndex);
        if (stack == null || stack.Count <= 1)
            return false;

        InventoryItemDefinition definition = ItemCatalog.Get(stack.ItemId);
        if (definition.MaxStack <= 1)
            return false;

        int freeSlot = FindFirstFreeSlot(definition, stack.Rotated);
        if (freeSlot < 0)
            return false;

        splitCount = Math.Clamp(amount, 1, stack.Count - 1);
        stack.Remove(splitCount);
        _stacks.Add(new InventoryItemStack(stack.ItemId, splitCount, freeSlot, stack.Rotated));
        newSlotIndex = freeSlot;
        return true;
    }

    public IEnumerable<int> CoveredSlots(InventoryItemStack stack)
        => CoveredSlots(stack.SlotIndex, ItemCatalog.Get(stack.ItemId), stack.Rotated);

    public IEnumerable<int> CoveredSlots(InventoryItemStack stack, bool rotated)
        => CoveredSlots(stack.SlotIndex, ItemCatalog.Get(stack.ItemId), rotated);

    public int GetSlotWidth(InventoryItemStack stack)
        => GetSlotWidth(ItemCatalog.Get(stack.ItemId), stack.Rotated);

    public int GetSlotHeight(InventoryItemStack stack)
        => GetSlotHeight(ItemCatalog.Get(stack.ItemId), stack.Rotated);

    public int GetSlotWidth(InventoryItemDefinition definition, bool rotated)
        => rotated ? definition.SlotHeight : definition.SlotWidth;

    public int GetSlotHeight(InventoryItemDefinition definition, bool rotated)
        => rotated ? definition.SlotWidth : definition.SlotHeight;

    public bool CanRotateStackAtSlot(int slotIndex)
    {
        InventoryItemStack? stack = GetStackCoveringSlot(slotIndex);
        if (stack == null)
            return false;

        InventoryItemDefinition definition = ItemCatalog.Get(stack.ItemId);
        if (definition.SlotWidth == definition.SlotHeight)
            return false;

        return CanPlaceAt(definition, !stack.Rotated, stack.SlotIndex, stack);
    }

    public bool RotateStackAtSlot(int slotIndex)
    {
        InventoryItemStack? stack = GetStackCoveringSlot(slotIndex);
        if (stack == null || !CanRotateStackAtSlot(slotIndex))
            return false;

        stack.SetRotation(!stack.Rotated);
        return true;
    }

    private bool TryAddStackAt(string itemId, int count, int slotIndex, bool rotated)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            return false;

        InventoryItemDefinition definition = ItemCatalog.Get(itemId);
        if (count > definition.MaxStack)
            return false;

        if (!CanPlaceAt(definition, rotated, slotIndex, ignoredStack: null))
            return false;

        _stacks.Add(new InventoryItemStack(itemId, count, slotIndex, rotated));
        return true;
    }

    private int FindFirstFreeSlot(InventoryItemDefinition definition)
        => FindFirstFreeSlot(definition, rotated: false);

    private int FindFirstFreeSlot(InventoryItemDefinition definition, bool rotated)
    {
        for (int slot = 0; slot < SlotCapacity; slot++)
        {
            if (CanPlaceAt(definition, rotated, slot, ignoredStack: null))
                return slot;
        }

        return -1;
    }

    private static bool CanMerge(InventoryItemStack moving, InventoryItemStack target)
    {
        if (ReferenceEquals(moving, target) ||
            !string.Equals(moving.ItemId, target.ItemId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        InventoryItemDefinition definition = ItemCatalog.Get(moving.ItemId);
        return definition.MaxStack > 1 && target.Count < definition.MaxStack;
    }

    private bool TryMergeStacks(InventoryItemStack moving, InventoryItemStack target, out InventoryMoveResult result)
    {
        result = InventoryMoveResult.None;
        if (!CanMerge(moving, target))
        {
            InventoryItemDefinition definition = ItemCatalog.Get(moving.ItemId);
            if (!ReferenceEquals(moving, target) &&
                string.Equals(moving.ItemId, target.ItemId, StringComparison.OrdinalIgnoreCase) &&
                definition.MaxStack > 1 &&
                target.Count >= definition.MaxStack)
            {
                result = InventoryMoveResult.StackFull;
            }

            return false;
        }

        InventoryItemDefinition movingDefinition = ItemCatalog.Get(moving.ItemId);
        int room = movingDefinition.MaxStack - target.Count;
        if (room <= 0)
        {
            result = InventoryMoveResult.StackFull;
            return false;
        }

        int movingCountBefore = moving.Count;
        int movedCount = Math.Min(room, moving.Count);
        target.Add(movedCount);
        moving.Remove(movedCount);

        if (moving.Count <= 0)
        {
            _stacks.Remove(moving);
            result = InventoryMoveResult.Merged;
        }
        else
        {
            result = movedCount < movingCountBefore
                ? InventoryMoveResult.PartiallyMerged
                : InventoryMoveResult.Merged;
        }

        return true;
    }

    private bool CanPlaceAt(InventoryItemDefinition definition, bool rotated, int slotIndex, InventoryItemStack? ignoredStack)
    {
        if (slotIndex < 0 || slotIndex >= SlotCapacity)
            return false;

        int startX = slotIndex % GridWidth;
        int startY = slotIndex / GridWidth;
        if (startX + GetSlotWidth(definition, rotated) > GridWidth ||
            startY + GetSlotHeight(definition, rotated) > GridHeight)
            return false;

        foreach (InventoryItemStack stack in _stacks)
        {
            if (ReferenceEquals(stack, ignoredStack))
                continue;

            if (FootprintsOverlap(slotIndex, definition, rotated, stack.SlotIndex, ItemCatalog.Get(stack.ItemId), stack.Rotated))
                return false;
        }

        return true;
    }

    private bool CanSwap(InventoryItemStack moving, bool movingRotated, int movingTargetSlot, InventoryItemStack target)
    {
        InventoryItemDefinition movingDefinition = ItemCatalog.Get(moving.ItemId);
        InventoryItemDefinition targetDefinition = ItemCatalog.Get(target.ItemId);
        int movingOriginalSlot = moving.SlotIndex;
        int targetOriginalSlot = target.SlotIndex;

        return CanPlaceAt(movingDefinition, movingRotated, movingTargetSlot, moving, target) &&
               CanPlaceAt(targetDefinition, target.Rotated, movingOriginalSlot, target, moving) &&
               !FootprintsOverlap(movingTargetSlot, movingDefinition, movingRotated, movingOriginalSlot, targetDefinition, target.Rotated);
    }

    private bool CanPlaceAt(
        InventoryItemDefinition definition,
        bool rotated,
        int slotIndex,
        InventoryItemStack? ignoredA,
        InventoryItemStack? ignoredB)
    {
        if (slotIndex < 0 || slotIndex >= SlotCapacity)
            return false;

        int startX = slotIndex % GridWidth;
        int startY = slotIndex / GridWidth;
        if (startX + GetSlotWidth(definition, rotated) > GridWidth ||
            startY + GetSlotHeight(definition, rotated) > GridHeight)
            return false;

        foreach (InventoryItemStack stack in _stacks)
        {
            if (ReferenceEquals(stack, ignoredA) || ReferenceEquals(stack, ignoredB))
                continue;

            if (FootprintsOverlap(slotIndex, definition, rotated, stack.SlotIndex, ItemCatalog.Get(stack.ItemId), stack.Rotated))
                return false;
        }

        return true;
    }

    private bool FootprintsOverlap(
        int aSlot,
        InventoryItemDefinition a,
        bool aRotated,
        int bSlot,
        InventoryItemDefinition b,
        bool bRotated)
    {
        int ax = aSlot % GridWidth;
        int ay = aSlot / GridWidth;
        int bx = bSlot % GridWidth;
        int by = bSlot / GridWidth;

        return ax < bx + GetSlotWidth(b, bRotated) &&
               ax + GetSlotWidth(a, aRotated) > bx &&
               ay < by + GetSlotHeight(b, bRotated) &&
               ay + GetSlotHeight(a, aRotated) > by;
    }

    private bool FootprintContainsSlot(int originSlot, InventoryItemDefinition definition, bool rotated, int slotIndex)
        => CoveredSlots(originSlot, definition, rotated).Contains(slotIndex);

    private IEnumerable<int> CoveredSlots(int originSlot, InventoryItemDefinition definition, bool rotated)
    {
        int originX = originSlot % GridWidth;
        int originY = originSlot / GridWidth;
        int width = GetSlotWidth(definition, rotated);
        int height = GetSlotHeight(definition, rotated);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
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
