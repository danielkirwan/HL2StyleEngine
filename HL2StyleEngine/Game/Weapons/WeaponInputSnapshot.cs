namespace Game.Weapons;

internal readonly struct WeaponInputSnapshot
{
    public WeaponInputSnapshot(bool primaryPressed, bool primaryHeld, bool secondaryPressed, bool switchPressed)
    {
        PrimaryPressed = primaryPressed;
        PrimaryHeld = primaryHeld;
        SecondaryPressed = secondaryPressed;
        SwitchPressed = switchPressed;
    }

    public bool PrimaryPressed { get; }
    public bool PrimaryHeld { get; }
    public bool SecondaryPressed { get; }
    public bool SwitchPressed { get; }
}
