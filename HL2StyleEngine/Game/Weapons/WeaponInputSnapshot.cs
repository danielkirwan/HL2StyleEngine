namespace Game.Weapons;

internal readonly struct WeaponInputSnapshot
{
    public WeaponInputSnapshot(bool primaryPressed, bool secondaryPressed, bool switchPressed)
    {
        PrimaryPressed = primaryPressed;
        SecondaryPressed = secondaryPressed;
        SwitchPressed = switchPressed;
    }

    public bool PrimaryPressed { get; }
    public bool SecondaryPressed { get; }
    public bool SwitchPressed { get; }
}
