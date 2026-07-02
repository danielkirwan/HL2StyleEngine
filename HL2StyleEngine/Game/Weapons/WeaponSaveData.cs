namespace Game.Weapons;

internal sealed class WeaponSaveData
{
    public string WeaponId { get; set; } = "";
    public bool Owned { get; set; }
    public int CurrentMagazine { get; set; }
    public int ReserveAmmo { get; set; }
}