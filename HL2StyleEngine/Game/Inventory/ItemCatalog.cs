namespace Game.Inventory;

public static class ItemCatalog
{
    public const string InkRibbon = "InkRibbon";

    private static readonly Dictionary<string, InventoryItemDefinition> KnownItems = new(StringComparer.OrdinalIgnoreCase)
    {
        [InkRibbon] = new(
            InkRibbon,
            "Ink Ribbon",
            "A ribbon used with a typewriter to record progress.",
            InventoryItemType.Consumable,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 6),

        ["RustedKey"] = new(
            "RustedKey",
            "Rusted Key",
            "An old key with a rusted bit. It may still open a simple lock.",
            InventoryItemType.Key,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 1,
            expiresWhenMatchingLocksOpened: true),

        ["ServiceKey"] = new(
            "ServiceKey",
            "Service Key",
            "A utility key for service rooms and supply boxes.",
            InventoryItemType.Key,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 1,
            expiresWhenMatchingLocksOpened: true),

        ["ArchiveKey"] = new(
            "ArchiveKey",
            "Archive Key",
            "A key marked for the archive wing.",
            InventoryItemType.Key,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 1,
            expiresWhenMatchingLocksOpened: true),

        ["Scrap"] = new(
            "Scrap",
            "Scrap",
            "Crafting material. Combine it later to create or improve useful items.",
            InventoryItemType.Material,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 99),

        ["CrankHandle"] = new(
            "CrankHandle",
            "Crank Handle",
            "A sturdy handle for a square crank mechanism.",
            InventoryItemType.Puzzle,
            slotWidth: 1,
            slotHeight: 2,
            maxStack: 1)
    };

    public static InventoryItemDefinition Get(string itemId)
    {
        if (KnownItems.TryGetValue(itemId, out InventoryItemDefinition? definition))
            return definition;

        InventoryItemType type = itemId.Contains("Key", StringComparison.OrdinalIgnoreCase)
            ? InventoryItemType.Key
            : InventoryItemType.Misc;

        return new InventoryItemDefinition(
            itemId,
            Prettify(itemId),
            "",
            type,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: type == InventoryItemType.Key ? 1 : 99,
            expiresWhenMatchingLocksOpened: type == InventoryItemType.Key);
    }

    public static string GetDisplayName(string itemId)
        => Get(itemId).DisplayName;

    public static string Prettify(string token)
    {
        token = token.Replace('_', ' ').Trim();
        if (token.Length == 0)
            return "Item";

        List<char> chars = new(token.Length + 4);
        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(token[i - 1]))
                chars.Add(' ');

            chars.Add(c);
        }

        return new string(chars.ToArray());
    }
}
