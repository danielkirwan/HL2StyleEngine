namespace Game.Weapons;

internal readonly struct WeaponInputSnapshot
{
    public WeaponInputSnapshot(
        bool primaryPressed,
        bool primaryHeld,
        bool secondaryPressed,
        int categorySlotPressed)
    {
        PrimaryPressed = primaryPressed;
        PrimaryHeld = primaryHeld;
        SecondaryPressed = secondaryPressed;
        CategorySlotPressed = Math.Clamp(categorySlotPressed, 0, 4);
    }

    public bool PrimaryPressed { get; }
    public bool PrimaryHeld { get; }
    public bool SecondaryPressed { get; }
    public int CategorySlotPressed { get; }
}