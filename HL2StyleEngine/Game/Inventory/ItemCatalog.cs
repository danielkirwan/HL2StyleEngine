namespace Game.Inventory;

public static class ItemCatalog
{
    public const string InkRibbon = "InkRibbon";
    public const string MasterKey = "MasterKey";
    public const string Scrap = "Scrap";
    public const string Gunpowder = "Gunpowder";
    public const string Bullets = "Bullets";
    public const string TestPistol = "TestPistol";
    public const string GravityGun = "GravityGun";
    public const string Crowbar = "Crowbar";
    public const string CrankHandle = "CrankHandle";
    public const string Fuse = "Fuse";

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

        [MasterKey] = new(
            MasterKey,
            "Test Key",
            "A developer test key that can open any prototype lock.",
            InventoryItemType.Key,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 1),

        [Scrap] = new(
            Scrap,
            "Scrap",
            "Crafting material. Combine it later to create or improve useful items.",
            InventoryItemType.Material,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 99),

        [Gunpowder] = new(
            Gunpowder,
            "Gunpowder",
            "A volatile powder. Combine it with scrap to make test ammunition.",
            InventoryItemType.Material,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 30),

        [Bullets] = new(
            Bullets,
            "Bullets",
            "Prototype ammunition made by combining scrap with gunpowder.",
            InventoryItemType.Ammo,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 60),

        [TestPistol] = new(
            TestPistol,
            "Test Pistol",
            "A simple prototype sidearm. Uses bullets from your inventory.",
            InventoryItemType.Weapon,
            slotWidth: 2,
            slotHeight: 1,
            maxStack: 1),

        [GravityGun] = new(
            GravityGun,
            "Gravity Gun",
            "A prototype physics manipulator for grabbing and launching dynamic objects.",
            InventoryItemType.Weapon,
            slotWidth: 3,
            slotHeight: 2,
            maxStack: 1),

        [Crowbar] = new(
            Crowbar,
            "Crowbar",
            "A heavy iron pry bar for close-range melee hits and crate smashing.",
            InventoryItemType.Weapon,
            slotWidth: 1,
            slotHeight: 3,
            maxStack: 1),

        [CrankHandle] = new(
            CrankHandle,
            "Crank Handle",
            "A sturdy handle for a square crank mechanism.",
            InventoryItemType.Puzzle,
            slotWidth: 1,
            slotHeight: 2,
            maxStack: 1),

        [Fuse] = new(
            Fuse,
            "Fuse",
            "An old ceramic fuse. It might restore power to a small mechanism.",
            InventoryItemType.Puzzle,
            slotWidth: 1,
            slotHeight: 1,
            maxStack: 1)
    };

    private static readonly IReadOnlyList<InventoryCombineRecipe> CombineRecipes =
    [
        new(
            Scrap,
            Gunpowder,
            Bullets,
            resultCount: 12,
            displayName: "Craft Bullets",
            description: "Combines scrap metal and gunpowder into a small batch of prototype ammunition.")
    ];

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

    public static bool CanCombine(string firstItemId, string secondItemId)
        => TryGetCombineRecipe(firstItemId, secondItemId, out _, out _, out _);

    public static bool TryGetCombineRecipe(
        string firstItemId,
        string secondItemId,
        out InventoryCombineRecipe? recipe,
        out int firstConsumedCount,
        out int secondConsumedCount)
    {
        foreach (InventoryCombineRecipe candidate in CombineRecipes)
        {
            if (!candidate.Matches(firstItemId, secondItemId, out firstConsumedCount, out secondConsumedCount))
                continue;

            recipe = candidate;
            return true;
        }

        recipe = null;
        firstConsumedCount = 0;
        secondConsumedCount = 0;
        return false;
    }

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
