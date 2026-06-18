using Engine.Render;
using System.Numerics;

namespace Game.Weapons;

internal sealed class WeaponSystem
{
    private readonly IReadOnlyList<WeaponDefinition> _definitions;
    private int _equippedIndex;
    private float _cooldownTimer;
    private float _flashTimer;
    private float _traceTimer;
    private Vector3 _traceStart;
    private Vector3 _traceEnd;
    private Vector4 _traceColor = new(1f, 0.84f, 0.42f, 1f);

    private WeaponSystem(IReadOnlyList<WeaponDefinition> definitions)
    {
        _definitions = definitions;
        _equippedIndex = Math.Max(0, IndexOfInventoryItem(Game.Inventory.ItemCatalog.GravityGun));
    }

    public static WeaponSystem CreatePrototypeLoadout()
        => new(WeaponDefinitions.All);

    public WeaponDefinition EquippedWeapon => _definitions[_equippedIndex];

    public void Update(float dt)
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer = MathF.Max(0f, _cooldownTimer - dt);
        if (_flashTimer > 0f)
            _flashTimer = MathF.Max(0f, _flashTimer - dt);
        if (_traceTimer > 0f)
            _traceTimer = MathF.Max(0f, _traceTimer - dt);
    }

    public bool TryEquipInventoryItem(IWeaponHost host, string itemId, bool showMessage)
    {
        int index = IndexOfInventoryItem(itemId);
        if (index < 0)
            return false;

        if (!host.HasInventoryItem(_definitions[index].InventoryItemId))
        {
            if (showMessage)
                host.ShowWeaponMessage($"{_definitions[index].DisplayName} is not in your inventory.", 1.25f);
            return true;
        }

        SetEquippedWeapon(host, index, showMessage);
        return true;
    }

    public void HandleInput(IWeaponHost host, WeaponInputSnapshot input)
    {
        EnsureEquippedWeaponOwned(host);

        if (host.HasHeldObject)
        {
            HandleHeldObjectInput(host, input);
            return;
        }

        if (input.SwitchPressed)
            CycleEquippedWeapon(host);

        if (!input.PrimaryPressed)
            return;

        WeaponDefinition weapon = EquippedWeapon;
        if (!host.HasInventoryItem(weapon.InventoryItemId))
        {
            host.ShowWeaponMessage($"No {weapon.DisplayName} equipped.", 1.25f);
            return;
        }

        if (weapon.Kind == WeaponKind.GravityGun)
            FireGravityGun(host, weapon);
        else
            FireHitscan(host, weapon);
    }

    public void Render(IWeaponHost host, Renderer renderer, bool visible)
    {
        if (!visible)
            return;

        if (_traceTimer > 0f)
            host.DrawWeaponBeam(renderer, _traceStart, _traceEnd, 0.025f, _traceColor);

        if (host.HasHeldObject && !host.HeldObjectGrabbedByGravityGun)
            return;

        WeaponDefinition weapon = EquippedWeapon;
        if (!host.HasInventoryItem(weapon.InventoryItemId))
            return;

        DrawViewModel(host, renderer, weapon);
    }

    private void HandleHeldObjectInput(IWeaponHost host, WeaponInputSnapshot input)
    {
        if (input.PrimaryPressed)
        {
            if (host.HeldObjectGrabbedByGravityGun)
            {
                WeaponDefinition gravityGun = FindGravityGun();
                Vector3 origin = host.CameraPosition;
                Vector3 dir = Vector3.Normalize(host.CameraForward);
                Vector3 traceEnd = host.TryGetHeldObjectCenter(out Vector3 heldCenter)
                    ? heldCenter
                    : origin + dir * host.HoldDistance;
                SetTrace(origin + dir * 0.55f, traceEnd, new Vector4(0.45f, 0.95f, 1f, 1f), 0.10f);
                _flashTimer = MathF.Max(_flashTimer, gravityGun.FlashSeconds);
                host.ThrowHeldObject(gravityGun.ThrowSpeed);
                host.ShowWeaponMessage("Gravity gun launched object.", 0.9f);
            }
            else
            {
                host.ThrowHeldObject(speed: 12f);
                host.ShowWeaponMessage("Threw held object.", 0.9f);
            }

            return;
        }

        if (input.SecondaryPressed)
        {
            host.DropHeldObject();
            host.ShowWeaponMessage("Dropped held object.", 0.9f);
        }
    }

    private void FireHitscan(IWeaponHost host, WeaponDefinition weapon)
    {
        if (_cooldownTimer > 0f)
            return;

        if (!TryConsumeAmmo(host, weapon))
            return;

        _cooldownTimer = weapon.CooldownSeconds;
        _flashTimer = weapon.FlashSeconds;

        Vector3 origin = host.CameraPosition;
        Vector3 dir = Vector3.Normalize(host.CameraForward);
        Vector3 end = origin + dir * weapon.Range;

        if (host.TryRaycastWeaponTarget(weapon.Range, out WeaponTargetHit hit))
        {
            end = hit.HitPoint;
            host.ApplyWeaponImpulse(hit.Target, dir * weapon.Impulse, hit.HitPoint, spinScale: 0.8f);
            host.ShowWeaponMessage($"{weapon.DisplayName} hit {host.GetEntityDisplayName(hit.Target)}.", 0.75f);
        }

        SetTrace(origin + dir * 0.65f, end, new Vector4(1f, 0.85f, 0.38f, 0.95f), 0.055f);
    }

    private void FireGravityGun(IWeaponHost host, WeaponDefinition weapon)
    {
        if (_cooldownTimer > 0f)
            return;

        _cooldownTimer = weapon.CooldownSeconds;
        _flashTimer = weapon.FlashSeconds;

        Vector3 origin = host.CameraPosition;
        Vector3 dir = Vector3.Normalize(host.CameraForward);

        Engine.Runtime.Entities.Entity? pickable = host.RaycastPickable(weapon.PickupRange, weapon.PickupMaxMass);
        if (pickable != null)
        {
            if (host.TryGetEntityCenter(pickable, out Vector3 center))
            {
                float distance = Vector3.Distance(origin, center);
                host.HoldDistance = Math.Clamp(distance, 2.0f, 4.8f);
                SetTrace(origin + dir * 0.55f, center, new Vector4(0.35f, 0.9f, 1f, 0.95f), 0.12f);
            }

            host.PickUpWithWeapon(pickable, grabbedByGravityGun: true);
            host.ShowWeaponMessage($"Gravity gun grabbed {host.GetEntityDisplayName(pickable)}.", 0.9f);
            return;
        }

        if (host.TryRaycastWeaponTarget(weapon.PickupRange, out WeaponTargetHit hit))
        {
            host.ApplyWeaponImpulse(hit.Target, dir * weapon.Impulse, hit.HitPoint, spinScale: 1.1f);
            SetTrace(origin + dir * 0.55f, hit.HitPoint, new Vector4(0.35f, 0.9f, 1f, 0.95f), 0.08f);
            host.ShowWeaponMessage($"Gravity pulse hit {host.GetEntityDisplayName(hit.Target)}.", 0.75f);
            return;
        }

        SetTrace(origin + dir * 0.55f, origin + dir * weapon.PickupRange, new Vector4(0.25f, 0.65f, 1f, 0.7f), 0.05f);
    }

    private bool TryConsumeAmmo(IWeaponHost host, WeaponDefinition weapon)
    {
        if (!weapon.UsesAmmo || weapon.AmmoItemId == null)
            return true;

        int ammoCount = host.GetInventoryItemCount(weapon.AmmoItemId);
        if (ammoCount < weapon.AmmoPerPrimaryFire)
        {
            _cooldownTimer = MathF.Max(_cooldownTimer, 0.20f);
            host.ShowWeaponMessage($"{weapon.DisplayName} is empty. Need {Game.Inventory.ItemCatalog.GetDisplayName(weapon.AmmoItemId)}.", 1.1f);
            return false;
        }

        return host.TryConsumeInventoryItem(weapon.AmmoItemId, weapon.AmmoPerPrimaryFire);
    }

    private void CycleEquippedWeapon(IWeaponHost host)
    {
        int next = FindNextOwnedWeapon(host);
        if (next < 0)
        {
            host.ShowWeaponMessage("No weapons in inventory.", 1.25f);
            return;
        }

        SetEquippedWeapon(host, next, showMessage: true);
    }

    private void SetEquippedWeapon(IWeaponHost host, int index, bool showMessage)
    {
        if (index < 0 || index >= _definitions.Count)
            return;

        if (_equippedIndex == index)
        {
            if (showMessage)
                host.ShowWeaponMessage($"{EquippedWeapon.DisplayName} already equipped.", 0.9f);
            return;
        }

        if (host.HasHeldObject)
            host.DropHeldObject();

        _equippedIndex = index;
        if (showMessage)
            host.ShowWeaponMessage($"Equipped {EquippedWeapon.DisplayName}.", 1.25f);
    }

    private void EnsureEquippedWeaponOwned(IWeaponHost host)
    {
        if (host.HasInventoryItem(EquippedWeapon.InventoryItemId))
            return;

        int next = FindNextOwnedWeapon(host);
        if (next >= 0)
            _equippedIndex = next;
    }

    private int FindNextOwnedWeapon(IWeaponHost host)
    {
        for (int offset = 1; offset <= _definitions.Count; offset++)
        {
            int index = (_equippedIndex + offset) % _definitions.Count;
            if (host.HasInventoryItem(_definitions[index].InventoryItemId))
                return index;
        }

        return -1;
    }

    private WeaponDefinition FindGravityGun()
        => _definitions.First(static weapon => weapon.Kind == WeaponKind.GravityGun);

    private int IndexOfInventoryItem(string itemId)
    {
        for (int i = 0; i < _definitions.Count; i++)
        {
            if (string.Equals(_definitions[i].InventoryItemId, itemId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void SetTrace(Vector3 start, Vector3 end, Vector4 color, float seconds)
    {
        _traceStart = start;
        _traceEnd = end;
        _traceColor = color;
        _traceTimer = seconds;
    }

    private void DrawViewModel(IWeaponHost host, Renderer renderer, WeaponDefinition weapon)
    {
        ViewModelBasis basis = BuildViewModelBasis(host);
        float recoil = _flashTimer > 0f ? -0.07f : 0f;

        foreach (WeaponViewModelPart part in weapon.ViewModel.FallbackParts)
        {
            if (part.OnlyDuringFlash && _flashTimer <= 0f)
                continue;

            Vector3 localOffset = part.LocalOffset;
            if (part.AffectedByRecoil)
                localOffset.Z += recoil;

            Vector4 color = part.Color;
            if (part.BrightenDuringFlash && _flashTimer > 0f)
                color.W = 1f;

            Vector3 position = ToViewModelWorld(host, basis, localOffset);
            if (part.Shape == WeaponViewModelPartShape.Sphere)
                host.DrawWeaponSphere(renderer, position, part.Radius, color);
            else
                host.DrawWeaponBox(renderer, position, part.Size, basis.Rotation, color);
        }

        if (weapon.Kind == WeaponKind.GravityGun &&
            host.HeldObjectGrabbedByGravityGun &&
            host.TryGetHeldObjectCenter(out Vector3 heldCenter))
        {
            Vector3 muzzle = ToViewModelWorld(host, basis, weapon.ViewModel.MuzzleOffset);
            host.DrawWeaponBeam(renderer, muzzle, heldCenter, 0.018f, new Vector4(0.35f, 0.88f, 1f, 0.75f));
        }
    }

    private readonly struct ViewModelBasis
    {
        public ViewModelBasis(Vector3 right, Vector3 up, Vector3 forward, Quaternion rotation)
        {
            Right = right;
            Up = up;
            Forward = forward;
            Rotation = rotation;
        }

        public Vector3 Right { get; }
        public Vector3 Up { get; }
        public Vector3 Forward { get; }
        public Quaternion Rotation { get; }
    }

    private static ViewModelBasis BuildViewModelBasis(IWeaponHost host)
    {
        Vector3 forward = Vector3.Normalize(host.CameraForward);
        Vector3 right = Vector3.Cross(Vector3.UnitY, forward);
        if (right.LengthSquared() < 0.0001f)
            right = Vector3.UnitX;
        else
            right = Vector3.Normalize(right);

        Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));
        Matrix4x4 rotationMatrix = new(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);

        Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotationMatrix));
        return new ViewModelBasis(right, up, forward, rotation);
    }

    private static Vector3 ToViewModelWorld(IWeaponHost host, ViewModelBasis basis, Vector3 localOffset)
        => host.CameraPosition + (basis.Right * localOffset.X) + (basis.Up * localOffset.Y) + (basis.Forward * localOffset.Z);
}
