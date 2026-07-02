using Game.Inventory;
using System.Numerics;

namespace Game.Weapons;

internal readonly struct WeaponLoadoutItem
{
    public WeaponLoadoutItem(string itemId, int count)
    {
        ItemId = itemId;
        Count = count;
    }

    public string ItemId { get; }
    public int Count { get; }
}

internal static class WeaponDefinitions
{
    public static readonly IReadOnlyList<WeaponDefinition> All =
    [
        new(
            id: "gravity_gun",
            inventoryItemId: ItemCatalog.GravityGun,
            displayName: "Gravity Gun",
            kind: WeaponKind.GravityGun,
            category: WeaponCategory.UtilityAndMelee,
            categoryOrder: 1,
            viewModel: new WeaponViewModelDefinition(
                modelAssetPath: "Content/Models/ViewModels/gravitygun.glb",
                muzzleOffset: new Vector3(0.58f, -0.09f, 1.24f),
                fallbackParts:
                [
                    new(WeaponViewModelPartShape.Box, new Vector3(0.58f, -0.28f, 0.62f), new Vector3(0.30f, 0.20f, 0.46f), new Vector4(0.16f, 0.20f, 0.22f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.58f, -0.13f, 0.86f), new Vector3(0.18f, 0.14f, 0.42f), new Vector4(0.26f, 0.34f, 0.35f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.46f, -0.09f, 1.10f), new Vector3(0.08f, 0.08f, 0.34f), new Vector4(0.40f, 0.82f, 1f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.70f, -0.09f, 1.10f), new Vector3(0.08f, 0.08f, 0.34f), new Vector4(0.40f, 0.82f, 1f, 1f)),
                    new(WeaponViewModelPartShape.Sphere, new Vector3(0.58f, -0.12f, 0.92f), Vector3.Zero, new Vector4(0.25f, 0.82f, 1f, 0.78f), radius: 0.13f, brightenDuringFlash: true)
                ],
                modelLocalOffset: new Vector3(0.58f, -0.30f, 0.82f),
                modelLocalEulerDegrees: Vector3.Zero,
                modelScale: 0.23f,
                modelTint: Vector4.One),
            cooldownSeconds: 0.16f,
            flashSeconds: 0.12f,
            range: 14.0f,
            impulse: 85f * 1.35f,
            damage: 18f,
            pickupRange: 3.25f,
            pickupMaxMass: 75f,
            throwSpeed: 34f,
            attractionRange: 14.0f,
            pullAcceleration: 32.0f,
            pullMaxSpeed: 12.0f,
            holdDistance: 2.85f,
            iconLabel: "GRAVITY"),

        new(
            id: "test_pistol",
            inventoryItemId: ItemCatalog.TestPistol,
            displayName: "Test Pistol",
            kind: WeaponKind.Hitscan,
            category: WeaponCategory.SmallWeapons,
            categoryOrder: 0,
            viewModel: new WeaponViewModelDefinition(
                modelAssetPath: "Content/Models/ViewModels/test_pistol.glb",
                muzzleOffset: new Vector3(0.56f, -0.20f, 1.03f),
                fallbackParts:
                [
                    new(WeaponViewModelPartShape.Box, new Vector3(0.56f, -0.23f, 0.66f), new Vector3(0.16f, 0.13f, 0.58f), new Vector4(0.12f, 0.13f, 0.14f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.56f, -0.13f, 0.58f), new Vector3(0.18f, 0.10f, 0.36f), new Vector4(0.28f, 0.30f, 0.30f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.56f, -0.43f, 0.42f), new Vector3(0.16f, 0.36f, 0.14f), new Vector4(0.10f, 0.09f, 0.08f, 1f)),
                    new(WeaponViewModelPartShape.Sphere, new Vector3(0.56f, -0.20f, 1.03f), Vector3.Zero, new Vector4(1f, 0.76f, 0.25f, 1f), radius: 0.08f, onlyDuringFlash: true, affectedByRecoil: false)
                ],
                modelLocalOffset: new Vector3(0.56f, -0.28f, 0.78f),
                modelLocalEulerDegrees: new Vector3(0f, 90f, 0f),
                modelScale: 0.45f,
                modelTint: Vector4.One),
            cooldownSeconds: 0.22f,
            flashSeconds: 0.08f,
            range: 50f,
            impulse: 85f,
            damage: 25f,
            ammoItemId: ItemCatalog.Bullets,
            ammoPerPrimaryFire: 1,
            magazineSize: 18,
            iconLabel: "PISTOL"),

        new(
            id: "crowbar",
            inventoryItemId: ItemCatalog.Crowbar,
            displayName: "Crowbar",
            kind: WeaponKind.Melee,
            category: WeaponCategory.UtilityAndMelee,
            categoryOrder: 0,
            viewModel: new WeaponViewModelDefinition(
                modelAssetPath: "Content/Models/ViewModels/Crowbar.glb",
                muzzleOffset: new Vector3(0.62f, -0.20f, 1.02f),
                fallbackParts:
                [
                    new(WeaponViewModelPartShape.Box, new Vector3(0.62f, -0.36f, 0.82f), new Vector3(0.07f, 0.07f, 0.95f), new Vector4(0.42f, 0.08f, 0.06f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.62f, -0.30f, 1.30f), new Vector3(0.26f, 0.07f, 0.10f), new Vector4(0.18f, 0.18f, 0.18f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.50f, -0.31f, 1.22f), new Vector3(0.08f, 0.07f, 0.22f), new Vector4(0.18f, 0.18f, 0.18f, 1f))
                ],
                modelLocalOffset: new Vector3(0.62f, -0.42f, 0.82f),
                modelLocalEulerDegrees: new Vector3(0f, 13f, 0f),
                modelScale: 1.15f,
                modelTint: Vector4.One),
            cooldownSeconds: 0.55f,
            flashSeconds: 0.16f,
            range: 2.15f,
            impulse: 70f,
            damage: 35f,
            iconLabel: "CROWBAR")
    ];


    public static readonly IReadOnlyList<WeaponCategoryDefinition> Categories =
    [
        new(1, WeaponCategory.SmallWeapons, "Small Weapons", "Up"),
        new(2, WeaponCategory.MediumWeapons, "Medium Weapons", "Right"),
        new(3, WeaponCategory.UtilityAndMelee, "Crowbar / Gravity Gun", "Down"),
        new(4, WeaponCategory.ThrowablesAndHeavy, "Throwables / Heavy", "Left")
    ];
    public static readonly IReadOnlyList<WeaponLoadoutItem> DefaultPrototypeLoadout =
    [
        new(ItemCatalog.GravityGun, 1),
        new(ItemCatalog.TestPistol, 1),
        new(ItemCatalog.Crowbar, 1),
        new(ItemCatalog.Bullets, 24)
    ];
}