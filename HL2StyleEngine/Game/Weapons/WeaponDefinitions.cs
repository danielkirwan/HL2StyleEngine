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
            viewModel: new WeaponViewModelDefinition(
                modelAssetPath: "Content/Models/ViewModels/gravity_gun.glb",
                muzzleOffset: new Vector3(0.32f, -0.09f, 1.24f),
                fallbackParts:
                [
                    new(WeaponViewModelPartShape.Box, new Vector3(0.32f, -0.28f, 0.62f), new Vector3(0.30f, 0.20f, 0.46f), new Vector4(0.16f, 0.20f, 0.22f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.32f, -0.13f, 0.86f), new Vector3(0.18f, 0.14f, 0.42f), new Vector4(0.26f, 0.34f, 0.35f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.20f, -0.09f, 1.10f), new Vector3(0.08f, 0.08f, 0.34f), new Vector4(0.40f, 0.82f, 1f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.44f, -0.09f, 1.10f), new Vector3(0.08f, 0.08f, 0.34f), new Vector4(0.40f, 0.82f, 1f, 1f)),
                    new(WeaponViewModelPartShape.Sphere, new Vector3(0.32f, -0.12f, 0.92f), Vector3.Zero, new Vector4(0.25f, 0.82f, 1f, 0.78f), radius: 0.13f, brightenDuringFlash: true)
                ]),
            cooldownSeconds: 0.16f,
            flashSeconds: 0.12f,
            range: 7.0f,
            impulse: 85f * 1.35f,
            pickupRange: 7.0f,
            pickupMaxMass: 75f,
            throwSpeed: 34f),

        new(
            id: "test_pistol",
            inventoryItemId: ItemCatalog.TestPistol,
            displayName: "Test Pistol",
            kind: WeaponKind.Hitscan,
            viewModel: new WeaponViewModelDefinition(
                modelAssetPath: "Content/Models/ViewModels/test_pistol.glb",
                muzzleOffset: new Vector3(0.31f, -0.20f, 1.03f),
                fallbackParts:
                [
                    new(WeaponViewModelPartShape.Box, new Vector3(0.31f, -0.23f, 0.66f), new Vector3(0.16f, 0.13f, 0.58f), new Vector4(0.12f, 0.13f, 0.14f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.31f, -0.13f, 0.58f), new Vector3(0.18f, 0.10f, 0.36f), new Vector4(0.28f, 0.30f, 0.30f, 1f)),
                    new(WeaponViewModelPartShape.Box, new Vector3(0.31f, -0.43f, 0.42f), new Vector3(0.16f, 0.36f, 0.14f), new Vector4(0.10f, 0.09f, 0.08f, 1f)),
                    new(WeaponViewModelPartShape.Sphere, new Vector3(0.31f, -0.20f, 1.03f), Vector3.Zero, new Vector4(1f, 0.76f, 0.25f, 1f), radius: 0.08f, onlyDuringFlash: true, affectedByRecoil: false)
                ]),
            cooldownSeconds: 0.22f,
            flashSeconds: 0.08f,
            range: 50f,
            impulse: 85f,
            ammoItemId: ItemCatalog.Bullets,
            ammoPerPrimaryFire: 1)
    ];

    public static readonly IReadOnlyList<WeaponLoadoutItem> DefaultPrototypeLoadout =
    [
        new(ItemCatalog.GravityGun, 1),
        new(ItemCatalog.TestPistol, 1),
        new(ItemCatalog.Bullets, 24)
    ];
}
