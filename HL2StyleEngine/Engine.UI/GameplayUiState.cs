namespace Engine.UI;

public sealed class GameplayUiState
{
    public bool InventoryOpen { get; init; }
    public bool StorageOpen { get; init; }
    public bool ItemCollectedOpen { get; init; }
    public string InteractionPrompt { get; init; } = "";
    public string GameMessage { get; init; } = "";
    public int GridWidth { get; init; } = 8;
    public int GridHeight { get; init; } = 4;
    public int UsedSlotCount { get; init; }
    public int SelectedSlot { get; init; } = -1;
    public int SelectedStorageSlot { get; init; } = -1;
    public bool StorageFocusStorage { get; init; }
    public bool StorageTransferPickerOpen { get; init; }
    public int StorageTransferAmount { get; init; } = 1;
    public bool StorageTransferFromStorage { get; init; }
    public bool MovingInventoryItem { get; init; }
    public int MovingFromSlot { get; init; } = -1;
    public int MovingTargetSlot { get; init; } = -1;
    public bool MovingItemRotated { get; init; }
    public int MovingItemSlotWidth { get; init; } = 1;
    public int MovingItemSlotHeight { get; init; } = 1;
    public bool CanPlaceMovingItem { get; init; }
    public bool CanSwapMovingItem { get; init; }
    public bool CanMergeMovingItem { get; init; }
    public bool CombiningInventoryItem { get; init; }
    public int CombineSourceSlot { get; init; } = -1;
    public string CombinePreviewTitle { get; init; } = "";
    public string CombinePreviewDescription { get; init; } = "";
    public string CombinePreviewResultName { get; init; } = "";
    public int CombinePreviewResultCount { get; init; }
    public bool InventoryActionMenuOpen { get; init; }
    public IReadOnlyList<string> InventoryActionLabels { get; init; } = Array.Empty<string>();
    public int SelectedInventoryActionIndex { get; init; }
    public bool InventorySplitPickerOpen { get; init; }
    public int InventorySplitAmount { get; init; }
    public bool InventoryDiscardConfirmOpen { get; init; }
    public bool UsingInventoryItem { get; init; }
    public string UseTargetPrompt { get; init; } = "";
    public int SaveCount { get; init; }
    public IReadOnlyList<GameplayUiInventoryItem> InventoryItems { get; init; } = Array.Empty<GameplayUiInventoryItem>();
    public IReadOnlyList<GameplayUiInventoryItem> StorageItems { get; init; } = Array.Empty<GameplayUiInventoryItem>();
    public GameplayUiCollectedItem? CollectedItem { get; init; }
}

public sealed class GameplayUiInventoryItem
{
    public int SlotIndex { get; init; }
    public IReadOnlyList<int> CoveredSlots { get; init; } = Array.Empty<int>();
    public string Id { get; init; } = "";
    public string IconPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public int Count { get; init; } = 1;
    public int SlotWidth { get; init; } = 1;
    public int SlotHeight { get; init; } = 1;
    public bool Rotated { get; init; }
    public int MaxStack { get; init; } = 1;
    public bool IsCombineSource { get; init; }
    public bool IsValidCombineTarget { get; init; }
    public bool IsInvalidCombineTarget { get; init; }
    public bool IsValidUseTarget { get; init; }
    public bool IsInvalidUseTarget { get; init; }
    public bool IsUseCandidate { get; init; }
}

public sealed class GameplayUiCollectedItem
{
    public string Title { get; init; } = "Item Collected";
    public string Id { get; init; } = "";
    public string IconPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public int Count { get; init; } = 1;
    public int SlotWidth { get; init; } = 1;
    public int SlotHeight { get; init; } = 1;
    public int MaxStack { get; init; } = 1;
}
