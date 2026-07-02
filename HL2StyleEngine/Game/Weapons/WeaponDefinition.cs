using System.Numerics;

namespace Game.Weapons;

internal enum WeaponKind
{
    Hitscan,
    GravityGun,
    Melee
}

internal enum WeaponCategory
{
    SmallWeapons = 1,
    MediumWeapons = 2,
    UtilityAndMelee = 3,
    ThrowablesAndHeavy = 4
}

internal readonly struct WeaponCategoryDefinition
{
    public WeaponCategoryDefinition(int slot, WeaponCategory category, string displayName, string directionLabel)
    {
        Slot = slot;
        Category = category;
        DisplayName = displayName;
        DirectionLabel = directionLabel;
    }

    public int Slot { get; }
    public WeaponCategory Category { get; }
    public string DisplayName { get; }
    public string DirectionLabel { get; }
}

internal readonly struct WeaponAmmoSnapshot
{
    public WeaponAmmoSnapshot(int currentMagazine, int reserveAmmo, bool usesAmmo)
    {
        CurrentMagazine = Math.Max(0, currentMagazine);
        ReserveAmmo = Math.Max(0, reserveAmmo);
        UsesAmmo = usesAmmo;
    }

    public int CurrentMagazine { get; }
    public int ReserveAmmo { get; }
    public int TotalAmmo => CurrentMagazine + ReserveAmmo;
    public bool UsesAmmo { get; }
    public bool HasAmmo => !UsesAmmo || TotalAmmo > 0;
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
        ModelScale = modelScale;
        ModelTint = modelTint ?? Vector4.One;
    }

    public string? ModelAssetPath { get; }
    public Vector3 MuzzleOffset { get; set; }
    public IReadOnlyList<WeaponViewModelPart> FallbackParts { get; }
    public Vector3 ModelLocalOffset { get; set; }
    public Vector3 ModelLocalEulerDegrees { get; set; }
    private float _modelScale;
    public float ModelScale
    {
        get => _modelScale;
        set => _modelScale = MathF.Max(0.001f, value);
    }
    public Vector4 ModelTint { get; }
}

internal sealed class WeaponDefinition
{
    public WeaponDefinition(
        string id,
        string inventoryItemId,
        string displayName,
        WeaponKind kind,
        WeaponCategory category,
        int categoryOrder,
        WeaponViewModelDefinition viewModel,
        float cooldownSeconds,
        float flashSeconds,
        float range,
        float impulse = 0f,
        float damage = 0f,
        float pickupRange = 0f,
        float pickupMaxMass = 0f,
        float throwSpeed = 0f,
        float attractionRange = 0f,
        float pullAcceleration = 0f,
        float pullMaxSpeed = 0f,
        float holdDistance = 0f,
        string? ammoItemId = null,
        int ammoPerPrimaryFire = 0,
        int magazineSize = 0,
        string iconLabel = "")
    {
        Id = id;
        InventoryItemId = inventoryItemId;
        DisplayName = displayName;
        Kind = kind;
        Category = category;
        CategoryOrder = categoryOrder;
        ViewModel = viewModel;
        CooldownSeconds = cooldownSeconds;
        FlashSeconds = flashSeconds;
        Range = range;
        Impulse = impulse;
        Damage = MathF.Max(0f, damage);
        PickupRange = pickupRange;
        PickupMaxMass = pickupMaxMass;
        ThrowSpeed = throwSpeed;
        AttractionRange = attractionRange > 0f ? attractionRange : pickupRange;
        PullAcceleration = pullAcceleration;
        PullMaxSpeed = pullMaxSpeed;
        HoldDistance = holdDistance > 0f ? holdDistance : MathF.Min(3.0f, pickupRange);
        AmmoItemId = ammoItemId;
        AmmoPerPrimaryFire = Math.Max(0, ammoPerPrimaryFire);
        MagazineSize = Math.Max(0, magazineSize);
        IconLabel = string.IsNullOrWhiteSpace(iconLabel) ? displayName : iconLabel;
    }

    public string Id { get; }
    public string InventoryItemId { get; }
    public string DisplayName { get; }
    public WeaponKind Kind { get; }
    public WeaponCategory Category { get; }
    public int CategoryOrder { get; }
    public WeaponViewModelDefinition ViewModel { get; }
    public float CooldownSeconds { get; }
    public float FlashSeconds { get; }
    public float Range { get; }
    public float Impulse { get; }
    public float Damage { get; }
    public float PickupRange { get; }
    public float PickupMaxMass { get; }
    public float ThrowSpeed { get; }
    public float AttractionRange { get; }
    public float PullAcceleration { get; }
    public float PullMaxSpeed { get; }
    public float HoldDistance { get; }
    public string? AmmoItemId { get; }
    public int AmmoPerPrimaryFire { get; }
    public int MagazineSize { get; }
    public string IconLabel { get; }
    public bool UsesAmmo => !string.IsNullOrWhiteSpace(AmmoItemId) && AmmoPerPrimaryFire > 0;
}
