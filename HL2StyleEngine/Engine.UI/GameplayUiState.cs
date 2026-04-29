namespace Engine.UI;

public sealed class GameplayUiState
{
    public bool InventoryOpen { get; init; }
    public bool ItemCollectedOpen { get; init; }
    public string InteractionPrompt { get; init; } = "";
    public string GameMessage { get; init; } = "";
    public int GridWidth { get; init; } = 8;
    public int GridHeight { get; init; } = 4;
    public int UsedSlotCount { get; init; }
    public int SelectedSlot { get; init; } = -1;
    public int SaveCount { get; init; }
    public IReadOnlyList<GameplayUiInventoryItem> InventoryItems { get; init; } = Array.Empty<GameplayUiInventoryItem>();
    public GameplayUiCollectedItem? CollectedItem { get; init; }
}

public sealed class GameplayUiInventoryItem
{
    public int SlotIndex { get; init; }
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public int Count { get; init; } = 1;
    public int SlotWidth { get; init; } = 1;
    public int SlotHeight { get; init; } = 1;
    public int MaxStack { get; init; } = 1;
}

public sealed class GameplayUiCollectedItem
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Type { get; init; } = "";
    public int Count { get; init; } = 1;
    public int SlotWidth { get; init; } = 1;
    public int SlotHeight { get; init; } = 1;
    public int MaxStack { get; init; } = 1;
}
