using Engine.Render;
using Engine.Runtime.Entities;
using Game.Inventory;
using System.Numerics;

namespace Game.Weapons;

internal sealed class WeaponSystem
{
    private const float GravityBlastRange = 5.5f;
    private const float GravityBlastImpulseScale = 2.25f;
    private const float GravityBlastCooldownSeconds = 0.30f;
    private const float GravityStaticBurstSeconds = 0.14f;
    private const float MeleeSwingSeconds = 0.28f;
    private const float WeaponSelectorVisibleSeconds = 1.55f;

    private sealed class RuntimeWeaponState
    {
        public bool Owned;
        public int CurrentMagazine;
        public int ReserveAmmo;
    }

    private readonly IReadOnlyList<WeaponDefinition> _definitions;
    private readonly Dictionary<string, RuntimeWeaponState> _runtimeStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _ownedWeaponIds = new(StringComparer.OrdinalIgnoreCase);
    private int _equippedIndex;
    private float _cooldownTimer;
    private float _flashTimer;
    private float _traceTimer;
    private float _meleeSwingTimer;
    private int _meleeSwingVariant;
    private Vector3 _traceStart;
    private Vector3 _traceEnd;
    private Vector4 _traceColor = new(1f, 0.84f, 0.42f, 1f);
    private float _gravityStaticBurstTimer;
    private Vector3 _gravityStaticBurstStart;
    private Vector3 _gravityStaticBurstEnd;
    private int _gravityStaticBurstIndex;
    private Entity? _gravityPullTarget;
    private bool _gravityHeldLaunchArmed = true;
    private bool _gravitySuppressPullUntilPrimaryReleased;
    private float _weaponSelectorTimer;

    private WeaponSystem(IReadOnlyList<WeaponDefinition> definitions)
    {
        _definitions = definitions;
        foreach (WeaponDefinition weapon in _definitions)
            _runtimeStates[weapon.Id] = new RuntimeWeaponState();

        _equippedIndex = Math.Max(0, IndexOfInventoryItem(ItemCatalog.GravityGun));
    }

    public static WeaponSystem CreatePrototypeLoadout()
        => new(WeaponDefinitions.All);

    public IReadOnlyList<WeaponDefinition> Definitions => _definitions;
    public WeaponDefinition EquippedWeapon => _definitions[_equippedIndex];
    public bool WeaponSelectorVisible => _weaponSelectorTimer > 0f;

    public void Update(float dt)
    {
        if (_cooldownTimer > 0f)
            _cooldownTimer = MathF.Max(0f, _cooldownTimer - dt);
        if (_flashTimer > 0f)
            _flashTimer = MathF.Max(0f, _flashTimer - dt);
        if (_traceTimer > 0f)
            _traceTimer = MathF.Max(0f, _traceTimer - dt);
        if (_meleeSwingTimer > 0f)
            _meleeSwingTimer = MathF.Max(0f, _meleeSwingTimer - dt);
        if (_gravityStaticBurstTimer > 0f)
            _gravityStaticBurstTimer = MathF.Max(0f, _gravityStaticBurstTimer - dt);
        if (_weaponSelectorTimer > 0f)
            _weaponSelectorTimer = MathF.Max(0f, _weaponSelectorTimer - dt);
    }

    public bool IsWeaponOwned(WeaponDefinition weapon)
        => _ownedWeaponIds.Contains(weapon.Id);

    public bool IsWeaponEquipped(WeaponDefinition weapon)
        => string.Equals(EquippedWeapon.Id, weapon.Id, StringComparison.OrdinalIgnoreCase);

    public WeaponAmmoSnapshot GetAmmoSnapshot(WeaponDefinition weapon)
    {
        RuntimeWeaponState state = GetState(weapon);
        return new WeaponAmmoSnapshot(state.CurrentMagazine, state.ReserveAmmo, weapon.UsesAmmo);
    }

    public bool IsWeaponSystemInventoryItem(string itemId)
        => IndexOfInventoryItem(itemId) >= 0 || IsAmmoItem(itemId);

    public bool TryGrantInventoryItem(string itemId, int count, bool fillMagazineFromAmmo)
    {
        int weaponIndex = IndexOfInventoryItem(itemId);
        if (weaponIndex >= 0)
        {
            GrantWeapon(_definitions[weaponIndex]);
            EnsureEquippedWeaponOwned(null);
            return true;
        }

        return TryGrantAmmo(itemId, count, fillMagazineFromAmmo);
    }

    public void ResetToDefaultPrototypeLoadout(bool includeStarterAmmo)
    {
        _ownedWeaponIds.Clear();
        foreach (RuntimeWeaponState state in _runtimeStates.Values)
        {
            state.Owned = false;
            state.CurrentMagazine = 0;
            state.ReserveAmmo = 0;
        }

        EnsureDefaultPrototypeLoadout(includeStarterAmmo);
    }

    public void EnsureDefaultPrototypeLoadout(bool includeStarterAmmo)
    {
        foreach (WeaponLoadoutItem item in WeaponDefinitions.DefaultPrototypeLoadout)
        {
            int weaponIndex = IndexOfInventoryItem(item.ItemId);
            if (weaponIndex >= 0)
            {
                GrantWeapon(_definitions[weaponIndex]);
                continue;
            }

            if (includeStarterAmmo)
                TryGrantAmmo(item.ItemId, item.Count, fillMagazineFromAmmo: true);
        }

        EnsureEquippedWeaponOwned(null);
    }

    public IReadOnlyList<WeaponSaveData> ToSaveData()
        => _definitions
            .Select(weapon =>
            {
                RuntimeWeaponState state = GetState(weapon);
                return new WeaponSaveData
                {
                    WeaponId = weapon.Id,
                    Owned = state.Owned,
                    CurrentMagazine = state.CurrentMagazine,
                    ReserveAmmo = state.ReserveAmmo
                };
            })
            .ToList();

    public void LoadFromSave(IReadOnlyList<WeaponSaveData> savedWeapons)
    {
        _ownedWeaponIds.Clear();
        foreach (RuntimeWeaponState state in _runtimeStates.Values)
        {
            state.Owned = false;
            state.CurrentMagazine = 0;
            state.ReserveAmmo = 0;
        }

        foreach (WeaponSaveData saved in savedWeapons)
        {
            int index = IndexOfWeaponId(saved.WeaponId);
            if (index < 0)
                continue;

            WeaponDefinition weapon = _definitions[index];
            RuntimeWeaponState state = GetState(weapon);
            state.Owned = saved.Owned;
            state.CurrentMagazine = Math.Max(0, saved.CurrentMagazine);
            state.ReserveAmmo = Math.Max(0, saved.ReserveAmmo);
            if (state.Owned)
                _ownedWeaponIds.Add(weapon.Id);
        }

        EnsureEquippedWeaponOwned(null);
    }

    public bool TryEquipInventoryItem(IWeaponHost host, string itemId, bool showMessage)
    {
        int index = IndexOfInventoryItem(itemId);
        if (index < 0)
            return false;

        if (!IsWeaponOwned(_definitions[index]))
        {
            if (showMessage)
                host.ShowWeaponMessage($"{_definitions[index].DisplayName} is not available.", 1.25f);
            return true;
        }

        SetEquippedWeapon(host, index, showMessage, showAlreadyMessage: false);
        return true;
    }

    public void HandleInput(IWeaponHost host, WeaponInputSnapshot input, float dt)
    {
        EnsureEquippedWeaponOwned(host);

        if (input.CategorySlotPressed > 0)
            TrySelectCategorySlot(host, input.CategorySlotPressed);

        if (host.HasHeldObject)
        {
            _gravityPullTarget = null;
            HandleHeldObjectInput(host, input);
            return;
        }

        WeaponDefinition weapon = EquippedWeapon;
        if (!IsWeaponOwned(weapon))
        {
            if (input.PrimaryPressed)
                host.ShowWeaponMessage($"No {weapon.DisplayName} equipped.", 1.25f);
            return;
        }

        if (weapon.Kind == WeaponKind.GravityGun)
        {
            HandleGravityGunInput(host, weapon, input, dt);
        }
        else if (weapon.Kind == WeaponKind.Melee)
        {
            if (input.PrimaryPressed)
            {
                _gravityPullTarget = null;
                FireMelee(host, weapon);
            }
        }
        else if (input.PrimaryPressed)
        {
            _gravityPullTarget = null;
            FireHitscan(host, weapon);
        }
    }

    public void Render(IWeaponHost host, Renderer renderer, bool visible)
    {
        if (!visible)
            return;

        if (_traceTimer > 0f)
            host.DrawWeaponBeam(renderer, _traceStart, _traceEnd, 0.025f, _traceColor);
        if (_gravityStaticBurstTimer > 0f)
            DrawGravityStaticBurst(host, renderer);

        if (host.HasHeldObject && !host.HeldObjectGrabbedByGravityGun)
            return;

        WeaponDefinition weapon = EquippedWeapon;
        if (!IsWeaponOwned(weapon))
            return;

        DrawViewModel(host, renderer, weapon);
    }
    private void HandleHeldObjectInput(IWeaponHost host, WeaponInputSnapshot input)
    {
        if (host.HeldObjectGrabbedByGravityGun && !input.PrimaryHeld)
            _gravityHeldLaunchArmed = true;

        if (input.PrimaryPressed)
        {
            if (host.HeldObjectGrabbedByGravityGun)
            {
                if (!_gravityHeldLaunchArmed)
                    return;

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
                _gravityHeldLaunchArmed = true;
                _gravitySuppressPullUntilPrimaryReleased = true;
            }
            else
            {
                host.ThrowHeldObject(speed: 12f);
                host.ShowWeaponMessage("Threw held object.", 0.9f);
                _gravityPullTarget = null;
                _gravityHeldLaunchArmed = true;
                _gravitySuppressPullUntilPrimaryReleased = true;
            }

            return;
        }

        if (input.SecondaryPressed)
        {
            host.DropHeldObject();
            host.ShowWeaponMessage("Dropped held object.", 0.9f);
            _gravityHeldLaunchArmed = true;
            _gravitySuppressPullUntilPrimaryReleased = false;
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
            host.ApplyWeaponDamage(hit.Target, weapon.Damage, "Bullet");
            host.ShowWeaponMessage($"{weapon.DisplayName} hit {host.GetEntityDisplayName(hit.Target)}.", 0.75f);
        }

        SetTrace(origin + dir * 0.65f, end, new Vector4(1f, 0.85f, 0.38f, 0.95f), 0.055f);
    }

    private void FireMelee(IWeaponHost host, WeaponDefinition weapon)
    {
        if (_cooldownTimer > 0f)
            return;

        _cooldownTimer = weapon.CooldownSeconds;
        _flashTimer = weapon.FlashSeconds;
        _meleeSwingTimer = MeleeSwingSeconds;
        _meleeSwingVariant = (_meleeSwingVariant + 1) % 3;

        Vector3 origin = host.CameraPosition;
        Vector3 dir = Vector3.Normalize(host.CameraForward);
        Vector3 end = origin + dir * weapon.Range;

        if (host.TryRaycastWeaponTarget(weapon.Range, out WeaponTargetHit hit))
        {
            end = hit.HitPoint;
            bool pushed = host.ApplyWeaponImpulse(hit.Target, dir * weapon.Impulse, hit.HitPoint, spinScale: 0.7f);
            host.ApplyWeaponDamage(hit.Target, weapon.Damage, "Melee");
            host.ShowWeaponMessage(
                pushed
                    ? $"{weapon.DisplayName} struck {host.GetEntityDisplayName(hit.Target)}."
                    : $"{weapon.DisplayName} hit {host.GetEntityDisplayName(hit.Target)}.",
                0.75f);
        }

        SetTrace(origin + dir * 0.45f, end, new Vector4(0.95f, 0.92f, 0.78f, 0.9f), 0.05f);
    }

    private void HandleGravityGunInput(IWeaponHost host, WeaponDefinition weapon, WeaponInputSnapshot input, float dt)
    {
        if (input.SecondaryPressed)
        {
            FireGravityBlast(host, weapon);
            return;
        }

        if (!input.PrimaryHeld)
        {
            _gravityPullTarget = null;
            _gravitySuppressPullUntilPrimaryReleased = false;
            return;
        }

        if (_gravitySuppressPullUntilPrimaryReleased)
            return;

        Vector3 origin = host.CameraPosition;
        Vector3 dir = Vector3.Normalize(host.CameraForward);
        Entity? pickable = GetGravityPullTarget(host, weapon, origin);

        if (pickable != null)
        {
            Vector3 holdPoint = origin + dir * weapon.HoldDistance;
            if (!host.TryGetEntityCenter(pickable, out Vector3 center))
            {
                _gravityPullTarget = null;
                return;
            }

            float cameraDistance = Vector3.Distance(origin, center);
            float holdPointDistance = Vector3.Distance(center, holdPoint);
            if (cameraDistance <= weapon.PickupRange || holdPointDistance <= 0.55f)
            {
                host.HoldDistance = Math.Clamp(cameraDistance, 2.0f, MathF.Max(2.0f, weapon.HoldDistance));
                SetTrace(origin + dir * 0.55f, center, new Vector4(0.35f, 0.9f, 1f, 0.95f), 0.12f);
                _flashTimer = MathF.Max(_flashTimer, weapon.FlashSeconds);
                _gravityPullTarget = null;
                _gravityHeldLaunchArmed = false;
                host.PickUpWithWeapon(pickable, grabbedByGravityGun: true);
                host.ShowWeaponMessage($"Gravity gun grabbed {host.GetEntityDisplayName(pickable)}.", 0.9f);
                return;
            }

            if (host.TryApplyGravityGunAttraction(
                    pickable,
                    holdPoint,
                    dt,
                    weapon.PullAcceleration,
                    weapon.PullMaxSpeed,
                    out Vector3 pulledCenter,
                    out float mass,
                    out _))
            {
                _gravityPullTarget = pickable;
                _flashTimer = MathF.Max(_flashTimer, weapon.FlashSeconds * 0.65f);
                SetTrace(origin + dir * 0.55f, pulledCenter, new Vector4(0.25f, 0.85f, 1f, 0.82f), 0.06f);

                if (input.PrimaryPressed)
                    host.ShowWeaponMessage($"Gravity gun pulling {host.GetEntityDisplayName(pickable)} ({mass:0.#} mass).", 0.9f);

                return;
            }

            _gravityPullTarget = null;
        }

        if (!input.PrimaryPressed || _cooldownTimer > 0f)
            return;

        _cooldownTimer = weapon.CooldownSeconds;
        _flashTimer = weapon.FlashSeconds;

        if (host.TryRaycastWeaponTarget(weapon.AttractionRange, out WeaponTargetHit hit))
        {
            host.ApplyWeaponImpulse(hit.Target, dir * weapon.Impulse, hit.HitPoint, spinScale: 1.1f);
            host.ApplyWeaponDamage(hit.Target, weapon.Damage, "GravityPulse");
            SetTrace(origin + dir * 0.55f, hit.HitPoint, new Vector4(0.35f, 0.9f, 1f, 0.95f), 0.08f);
            host.ShowWeaponMessage($"Gravity pulse hit {host.GetEntityDisplayName(hit.Target)}.", 0.75f);
            return;
        }

        SetTrace(origin + dir * 0.55f, origin + dir * weapon.AttractionRange, new Vector4(0.25f, 0.65f, 1f, 0.7f), 0.05f);
    }

    private void FireGravityBlast(IWeaponHost host, WeaponDefinition weapon)
    {
        if (_cooldownTimer > 0f)
            return;

        Vector3 origin = host.CameraPosition;
        Vector3 dir = Vector3.Normalize(host.CameraForward);
        Vector3 burstStart = origin + dir * 0.55f;
        Vector3 burstEnd = origin + dir * GravityBlastRange;

        _cooldownTimer = MathF.Max(weapon.CooldownSeconds, GravityBlastCooldownSeconds);
        _flashTimer = MathF.Max(_flashTimer, weapon.FlashSeconds * 1.3f);
        _gravityPullTarget = null;
        _gravityHeldLaunchArmed = true;
        _gravitySuppressPullUntilPrimaryReleased = false;

        if (host.TryRaycastWeaponTarget(GravityBlastRange, out WeaponTargetHit hit))
        {
            burstEnd = hit.HitPoint;
            bool pushed = host.ApplyWeaponImpulse(
                hit.Target,
                dir * weapon.Impulse * GravityBlastImpulseScale,
                hit.HitPoint,
                spinScale: 1.75f);
            host.ApplyWeaponDamage(hit.Target, weapon.Damage * 1.4f, "GravityBlast");

            SetGravityStaticBurst(burstStart, burstEnd);
            host.ShowWeaponMessage(
                pushed
                    ? $"Gravity blast punted {host.GetEntityDisplayName(hit.Target)}."
                    : "Gravity blast discharged.",
                0.75f);
            return;
        }

        SetGravityStaticBurst(burstStart, burstEnd);
        host.ShowWeaponMessage("Gravity blast discharged.", 0.65f);
    }

    private void SetGravityStaticBurst(Vector3 start, Vector3 end)
    {
        _gravityStaticBurstStart = start;
        _gravityStaticBurstEnd = end;
        _gravityStaticBurstTimer = GravityStaticBurstSeconds;
        _gravityStaticBurstIndex++;
        SetTrace(start, end, new Vector4(0.48f, 0.95f, 1f, 0.96f), GravityStaticBurstSeconds * 0.75f);
    }

    private void DrawGravityStaticBurst(IWeaponHost host, Renderer renderer)
    {
        Vector3 start = _gravityStaticBurstStart;
        Vector3 end = _gravityStaticBurstEnd;
        Vector3 dir = end - start;
        float length = dir.Length();
        if (length <= 0.001f)
            return;

        dir /= length;
        Vector3 right = Vector3.Cross(Vector3.UnitY, dir);
        if (right.LengthSquared() < 0.0001f)
            right = Vector3.UnitX;
        else
            right = Vector3.Normalize(right);

        Vector3 up = Vector3.Normalize(Vector3.Cross(dir, right));
        float normalizedLife = Math.Clamp(_gravityStaticBurstTimer / GravityStaticBurstSeconds, 0f, 1f);
        float amplitude = MathF.Min(0.16f, length * 0.035f) * normalizedLife;
        float phase = _gravityStaticBurstIndex * 1.618f + _gravityStaticBurstTimer * 80f;

        host.DrawWeaponBeam(renderer, start, end, 0.032f, new Vector4(0.72f, 0.98f, 1f, 0.95f));

        for (int arc = 0; arc < 3; arc++)
        {
            float arcPhase = phase + arc * 2.41f;
            Vector3 offsetA = (right * MathF.Sin(arcPhase) + up * MathF.Cos(arcPhase * 0.73f)) * amplitude;
            Vector3 offsetB = (right * MathF.Sin(arcPhase * 1.37f + 1.1f) + up * MathF.Cos(arcPhase + 0.6f)) * amplitude;
            Vector3 p0 = start;
            Vector3 p1 = Vector3.Lerp(start, end, 0.34f) + offsetA;
            Vector3 p2 = Vector3.Lerp(start, end, 0.68f) + offsetB;
            Vector3 p3 = end;
            Vector4 color = arc == 0
                ? new Vector4(0.42f, 0.90f, 1f, 0.80f)
                : new Vector4(0.78f, 0.98f, 1f, 0.58f);

            host.DrawWeaponBeam(renderer, p0, p1, 0.014f, color);
            host.DrawWeaponBeam(renderer, p1, p2, 0.014f, color);
            host.DrawWeaponBeam(renderer, p2, p3, 0.014f, color);
        }
    }

    private Entity? GetGravityPullTarget(IWeaponHost host, WeaponDefinition weapon, Vector3 origin)
    {
        if (_gravityPullTarget != null &&
            host.TryGetEntityCenter(_gravityPullTarget, out Vector3 currentCenter) &&
            Vector3.Distance(origin, currentCenter) <= weapon.AttractionRange + 1.0f)
        {
            return _gravityPullTarget;
        }

        _gravityPullTarget = null;
        return host.RaycastPickable(weapon.AttractionRange, weapon.PickupMaxMass);
    }

    private bool TryConsumeAmmo(IWeaponHost host, WeaponDefinition weapon)
    {
        if (!weapon.UsesAmmo || weapon.AmmoItemId == null)
            return true;

        RuntimeWeaponState state = GetState(weapon);
        if (state.CurrentMagazine < weapon.AmmoPerPrimaryFire && TryReloadFromReserve(weapon, state))
            host.ShowWeaponMessage($"Reloaded {weapon.DisplayName}.", 0.75f);

        if (state.CurrentMagazine < weapon.AmmoPerPrimaryFire)
        {
            _cooldownTimer = MathF.Max(_cooldownTimer, 0.20f);
            host.ShowWeaponMessage($"{weapon.DisplayName} is empty.", 1.1f);
            return false;
        }

        state.CurrentMagazine -= weapon.AmmoPerPrimaryFire;
        return true;
    }

    private bool TrySelectCategorySlot(IWeaponHost host, int categorySlot)
    {
        WeaponCategoryDefinition category = WeaponDefinitions.Categories
            .FirstOrDefault(definition => definition.Slot == categorySlot);
        if (category.Slot == 0)
            return false;

        List<int> indexes = _definitions
            .Select((weapon, index) => (weapon, index))
            .Where(pair => pair.weapon.Category == category.Category && IsWeaponOwned(pair.weapon))
            .OrderBy(pair => pair.weapon.CategoryOrder)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.index)
            .ToList();

        if (indexes.Count == 0)
            return false;

        int selectedIndex = indexes[0];
        int currentPosition = indexes.IndexOf(_equippedIndex);
        if (currentPosition >= 0 && indexes.Count > 1)
            selectedIndex = indexes[(currentPosition + 1) % indexes.Count];

        _weaponSelectorTimer = WeaponSelectorVisibleSeconds;
        SetEquippedWeapon(host, selectedIndex, showMessage: true, showAlreadyMessage: false);
        return true;
    }

    private void SetEquippedWeapon(IWeaponHost? host, int index, bool showMessage, bool showAlreadyMessage = true)
    {
        if (index < 0 || index >= _definitions.Count)
            return;

        if (_equippedIndex == index)
        {
            if (showMessage && showAlreadyMessage)
                host?.ShowWeaponMessage($"{EquippedWeapon.DisplayName} already equipped.", 0.9f);
            return;
        }

        if (host?.HasHeldObject == true)
            host.DropHeldObject();

        _gravityPullTarget = null;
        _gravityHeldLaunchArmed = true;
        _gravitySuppressPullUntilPrimaryReleased = false;
        _meleeSwingTimer = 0f;
        _equippedIndex = index;
        if (showMessage)
            host?.ShowWeaponMessage($"Equipped {EquippedWeapon.DisplayName}.", 1.25f);
    }

    private void EnsureEquippedWeaponOwned(IWeaponHost? host)
    {
        if (IsWeaponOwned(EquippedWeapon))
            return;

        int next = FindNextOwnedWeapon();
        if (next >= 0)
        {
            _equippedIndex = next;
            return;
        }

        host?.ShowWeaponMessage("No weapons available.", 1.25f);
    }

    private int FindNextOwnedWeapon()
    {
        for (int offset = 1; offset <= _definitions.Count; offset++)
        {
            int index = (_equippedIndex + offset) % _definitions.Count;
            if (IsWeaponOwned(_definitions[index]))
                return index;
        }

        return -1;
    }

    private void GrantWeapon(WeaponDefinition weapon)
    {
        RuntimeWeaponState state = GetState(weapon);
        state.Owned = true;
        _ownedWeaponIds.Add(weapon.Id);
    }

    private bool TryGrantAmmo(string ammoItemId, int count, bool fillMagazineFromAmmo)
    {
        if (string.IsNullOrWhiteSpace(ammoItemId) || count <= 0)
            return false;

        WeaponDefinition? weapon = _definitions.FirstOrDefault(candidate =>
            candidate.UsesAmmo &&
            string.Equals(candidate.AmmoItemId, ammoItemId, StringComparison.OrdinalIgnoreCase));
        if (weapon == null)
            return false;

        RuntimeWeaponState state = GetState(weapon);
        int remaining = count;
        if (fillMagazineFromAmmo && weapon.MagazineSize > 0)
        {
            int room = Math.Max(0, weapon.MagazineSize - state.CurrentMagazine);
            int loaded = Math.Min(room, remaining);
            state.CurrentMagazine += loaded;
            remaining -= loaded;
        }

        state.ReserveAmmo += remaining;
        return true;
    }

    private bool TryReloadFromReserve(WeaponDefinition weapon, RuntimeWeaponState state)
    {
        if (!weapon.UsesAmmo || weapon.MagazineSize <= 0 || state.ReserveAmmo <= 0)
            return false;

        int room = Math.Max(0, weapon.MagazineSize - state.CurrentMagazine);
        if (room <= 0)
            return false;

        int moved = Math.Min(room, state.ReserveAmmo);
        state.CurrentMagazine += moved;
        state.ReserveAmmo -= moved;
        return moved > 0;
    }

    private RuntimeWeaponState GetState(WeaponDefinition weapon)
    {
        if (_runtimeStates.TryGetValue(weapon.Id, out RuntimeWeaponState? state))
            return state;

        state = new RuntimeWeaponState();
        _runtimeStates[weapon.Id] = state;
        return state;
    }

    private bool IsAmmoItem(string itemId)
        => _definitions.Any(weapon =>
            weapon.UsesAmmo &&
            string.Equals(weapon.AmmoItemId, itemId, StringComparison.OrdinalIgnoreCase));

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

    private int IndexOfWeaponId(string weaponId)
    {
        for (int i = 0; i < _definitions.Count; i++)
        {
            if (string.Equals(_definitions[i].Id, weaponId, StringComparison.OrdinalIgnoreCase))
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
        Vector3 animationOffset = GetViewModelAnimationOffset(weapon);
        Vector3 animationEuler = GetViewModelAnimationEulerDegrees(weapon);
        Quaternion animatedRotation = CreateViewModelRotation(basis, animationEuler);

        if (!string.IsNullOrWhiteSpace(weapon.ViewModel.ModelAssetPath))
        {
            Vector3 modelOffset = weapon.ViewModel.ModelLocalOffset + animationOffset;
            modelOffset.Z += recoil;
            Matrix4x4 modelTransform =
                Matrix4x4.CreateScale(weapon.ViewModel.ModelScale) *
                CreateLocalRotation(weapon.ViewModel.ModelLocalEulerDegrees + animationEuler) *
                Matrix4x4.CreateFromQuaternion(basis.Rotation) *
                Matrix4x4.CreateTranslation(ToViewModelWorld(host, basis, modelOffset));

            if (host.TryDrawWeaponModel(renderer, weapon.ViewModel.ModelAssetPath, modelTransform, weapon.ViewModel.ModelTint))
            {
                DrawGravityGunHeldBeam(host, renderer, weapon, basis);
                return;
            }
        }

        foreach (WeaponViewModelPart part in weapon.ViewModel.FallbackParts)
        {
            if (part.OnlyDuringFlash && _flashTimer <= 0f)
                continue;

            Vector3 localOffset = part.LocalOffset + animationOffset;
            if (part.AffectedByRecoil)
                localOffset.Z += recoil;

            Vector4 color = part.Color;
            if (part.BrightenDuringFlash && _flashTimer > 0f)
                color.W = 1f;

            Vector3 position = ToViewModelWorld(host, basis, localOffset);
            if (part.Shape == WeaponViewModelPartShape.Sphere)
                host.DrawWeaponSphere(renderer, position, part.Radius, color);
            else
                host.DrawWeaponBox(renderer, position, part.Size, animatedRotation, color);
        }

        DrawGravityGunHeldBeam(host, renderer, weapon, basis);
    }

    private static Matrix4x4 CreateLocalRotation(Vector3 eulerDegrees)
    {
        const float DegToRad = MathF.PI / 180f;
        return Matrix4x4.CreateFromYawPitchRoll(
            eulerDegrees.Y * DegToRad,
            eulerDegrees.X * DegToRad,
            eulerDegrees.Z * DegToRad);
    }

    private Vector3 GetViewModelAnimationOffset(WeaponDefinition weapon)
    {
        if (weapon.Kind != WeaponKind.Melee || _meleeSwingTimer <= 0f)
            return Vector3.Zero;

        float progress = GetMeleeSwingProgress();
        float swing = GetMeleeSwingAmount(progress);
        float followThrough = MathF.Sin(progress * MathF.PI * 2f);

        return _meleeSwingVariant switch
        {
            1 => new Vector3(-0.18f * swing, 0.03f * swing, 0.18f * swing - 0.04f * followThrough),
            2 => new Vector3(-0.22f * swing, -0.10f * swing, 0.20f * swing - 0.05f * followThrough),
            _ => new Vector3(-0.26f * swing, -0.05f * swing, 0.22f * swing - 0.06f * followThrough)
        };
    }

    private Vector3 GetViewModelAnimationEulerDegrees(WeaponDefinition weapon)
    {
        if (weapon.Kind != WeaponKind.Melee || _meleeSwingTimer <= 0f)
            return Vector3.Zero;

        float progress = GetMeleeSwingProgress();
        float swing = GetMeleeSwingAmount(progress);
        float followThrough = MathF.Sin(progress * MathF.PI * 2f);

        return _meleeSwingVariant switch
        {
            1 => new Vector3(34f * swing, 24f * swing, -42f * swing + 8f * followThrough),
            2 => new Vector3(56f * swing, 18f * swing, -32f * swing + 6f * followThrough),
            _ => new Vector3(48f * swing, 32f * swing, -54f * swing + 10f * followThrough)
        };
    }

    private float GetMeleeSwingProgress()
        => 1f - Math.Clamp(_meleeSwingTimer / MeleeSwingSeconds, 0f, 1f);

    private static float GetMeleeSwingAmount(float progress)
    {
        float snap = MathF.Sin(Math.Clamp(progress / 0.45f, 0f, 1f) * MathF.PI * 0.5f);
        float recovery = SmoothStep(0.48f, 1f, progress);
        return snap * (1f - recovery);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Math.Clamp((value - edge0) / MathF.Max(0.0001f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static Quaternion CreateViewModelRotation(ViewModelBasis basis, Vector3 localEulerDegrees)
    {
        if (localEulerDegrees == Vector3.Zero)
            return basis.Rotation;

        Matrix4x4 rotation = CreateLocalRotation(localEulerDegrees) * Matrix4x4.CreateFromQuaternion(basis.Rotation);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
    }

    private void DrawGravityGunHeldBeam(IWeaponHost host, Renderer renderer, WeaponDefinition weapon, ViewModelBasis basis)
    {
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
        Vector3 rotationRight = Vector3.Cross(Vector3.UnitY, forward);
        if (rotationRight.LengthSquared() < 0.0001f)
            rotationRight = -Vector3.UnitX;
        else
            rotationRight = Vector3.Normalize(rotationRight);

        Vector3 up = Vector3.Normalize(Vector3.Cross(forward, rotationRight));
        // Keep rotation as a proper camera basis; only flip placement so local +X stays screen-right.
        Vector3 placementRight = -rotationRight;
        Matrix4x4 rotationMatrix = new(
            rotationRight.X, rotationRight.Y, rotationRight.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);

        Quaternion rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotationMatrix));
        return new ViewModelBasis(placementRight, up, forward, rotation);
    }

    private static Vector3 ToViewModelWorld(IWeaponHost host, ViewModelBasis basis, Vector3 localOffset)
        => host.CameraPosition + (basis.Right * localOffset.X) + (basis.Up * localOffset.Y) + (basis.Forward * localOffset.Z);
}
