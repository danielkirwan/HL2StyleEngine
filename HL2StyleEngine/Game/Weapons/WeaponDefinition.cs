using System.Numerics;

namespace Game.Weapons;

internal enum WeaponKind
{
    Hitscan,
    GravityGun
}

internal enum WeaponViewModelPartShape
{
    Box,
    Sphere
}

internal sealed class WeaponViewModelPart
{
    public WeaponViewModelPart(
        WeaponViewModelPartShape shape,
        Vector3 localOffset,
        Vector3 size,
        Vector4 color,
        float radius = 0f,
        bool onlyDuringFlash = false,
        bool affectedByRecoil = true,
        bool brightenDuringFlash = false)
    {
        Shape = shape;
        LocalOffset = localOffset;
        Size = size;
        Radius = radius;
        Color = color;
        OnlyDuringFlash = onlyDuringFlash;
        AffectedByRecoil = affectedByRecoil;
        BrightenDuringFlash = brightenDuringFlash;
    }

    public WeaponViewModelPartShape Shape { get; }
    public Vector3 LocalOffset { get; }
    public Vector3 Size { get; }
    public float Radius { get; }
    public Vector4 Color { get; }
    public bool OnlyDuringFlash { get; }
    public bool AffectedByRecoil { get; }
    public bool BrightenDuringFlash { get; }
}

internal sealed class WeaponViewModelDefinition
{
    public WeaponViewModelDefinition(
        string? modelAssetPath,
        Vector3 muzzleOffset,
        IReadOnlyList<WeaponViewModelPart> fallbackParts,
        Vector3? modelLocalOffset = null,
        Vector3? modelLocalEulerDegrees = null,
        float modelScale = 1f,
        Vector4? modelTint = null)
    {
        ModelAssetPath = modelAssetPath;
        MuzzleOffset = muzzleOffset;
        FallbackParts = fallbackParts;
        ModelLocalOffset = modelLocalOffset ?? fallbackParts.FirstOrDefault()?.LocalOffset ?? Vector3.Zero;
        ModelLocalEulerDegrees = modelLocalEulerDegrees ?? Vector3.Zero;
        ModelScale = MathF.Max(0.001f, modelScale);
        ModelTint = modelTint ?? Vector4.One;
    }

    public string? ModelAssetPath { get; }
    public Vector3 MuzzleOffset { get; }
    public IReadOnlyList<WeaponViewModelPart> FallbackParts { get; }
    public Vector3 ModelLocalOffset { get; }
    public Vector3 ModelLocalEulerDegrees { get; }
    public float ModelScale { get; }
    public Vector4 ModelTint { get; }
}

internal sealed class WeaponDefinition
{
    public WeaponDefinition(
        string id,
        string inventoryItemId,
        string displayName,
        WeaponKind kind,
        WeaponViewModelDefinition viewModel,
        float cooldownSeconds,
        float flashSeconds,
        float range,
        float impulse = 0f,
        float pickupRange = 0f,
        float pickupMaxMass = 0f,
        float throwSpeed = 0f,
        float attractionRange = 0f,
        float pullAcceleration = 0f,
        float pullMaxSpeed = 0f,
        float holdDistance = 0f,
        string? ammoItemId = null,
        int ammoPerPrimaryFire = 0)
    {
        Id = id;
        InventoryItemId = inventoryItemId;
        DisplayName = displayName;
        Kind = kind;
        ViewModel = viewModel;
        CooldownSeconds = cooldownSeconds;
        FlashSeconds = flashSeconds;
        Range = range;
        Impulse = impulse;
        PickupRange = pickupRange;
        PickupMaxMass = pickupMaxMass;
        ThrowSpeed = throwSpeed;
        AttractionRange = attractionRange > 0f ? attractionRange : pickupRange;
        PullAcceleration = pullAcceleration;
        PullMaxSpeed = pullMaxSpeed;
        HoldDistance = holdDistance > 0f ? holdDistance : MathF.Min(3.0f, pickupRange);
        AmmoItemId = ammoItemId;
        AmmoPerPrimaryFire = Math.Max(0, ammoPerPrimaryFire);
    }

    public string Id { get; }
    public string InventoryItemId { get; }
    public string DisplayName { get; }
    public WeaponKind Kind { get; }
    public WeaponViewModelDefinition ViewModel { get; }
    public float CooldownSeconds { get; }
    public float FlashSeconds { get; }
    public float Range { get; }
    public float Impulse { get; }
    public float PickupRange { get; }
    public float PickupMaxMass { get; }
    public float ThrowSpeed { get; }
    public float AttractionRange { get; }
    public float PullAcceleration { get; }
    public float PullMaxSpeed { get; }
    public float HoldDistance { get; }
    public string? AmmoItemId { get; }
    public int AmmoPerPrimaryFire { get; }
    public bool UsesAmmo => !string.IsNullOrWhiteSpace(AmmoItemId) && AmmoPerPrimaryFire > 0;
}
