using Engine.Physics.Dynamics;
using Engine.Render;
using Engine.Runtime.Entities;
using Game.Inventory;
using System.Numerics;

namespace Game;

public sealed partial class HL2GameModule
{
    private const string BreakableCrateIntactNodeName = "Cube_Cube_001";
    private const string BreakableCrateFractureToken = "_fracturepart";
    private const float BreakableCrateCrowbarDamage = 25f;
    private const float BreakableCrateBulletDamage = 34f;
    private const float BreakableCrateInstantBreakDamage = 9999f;
    private const float FractureDebrisLifetimeSeconds = 3f;
    private const float FractureDebrisFadeStartSeconds = 2f;
    private const float FractureDebrisGravity = 8.5f;
    private const float CrateRewardBaseDropChance = 0.20f;
    private const float CrateRewardLowHealthDropBonus = 0.30f;
    private const float CrateRewardLowSuitDropBonus = 0.20f;

    private readonly List<FractureDebrisPiece> _fractureDebrisPieces = new();

    private sealed class FractureDebrisPiece
    {
        public string ModelAssetPath = "";
        public readonly HashSet<string> HiddenPartKeys = new(StringComparer.OrdinalIgnoreCase);
        public Vector3 Position;
        public Vector3 LocalCenter;
        public Vector3 Size = Vector3.One;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 Velocity;
        public Vector3 AngularVelocity;
        public float GroundCenterY;
        public float Age;
        public float Lifetime = FractureDebrisLifetimeSeconds;
    }

    private void ApplyFractureVisualDamage(Entity entity, Vector3 hitPoint)
    {
        if (!UsesBreakableCrateFractureVisuals(entity))
            return;

        if (!TryGetReadyModel(entity.Render.ModelAssetPath, "fracture crate", out WeaponModelCacheEntry? entry) ||
            entry?.LoadedModel == null)
        {
            return;
        }

        entity.FractureVisualActive = true;
        if (TryFindNearestVisibleFracturePart(entity, entry.LoadedModel, entry.Bounds, hitPoint, out string partKey))
            entity.HiddenModelPartKeys.Add(partKey);
    }

    private float GetEffectiveEntityDamage(Entity entity, float amount, string damageKind)
    {
        if (!UsesBreakableCrateFractureVisuals(entity))
            return amount;

        if (damageKind.Contains("Shotgun", StringComparison.OrdinalIgnoreCase) ||
            damageKind.Contains("Explosion", StringComparison.OrdinalIgnoreCase))
        {
            return BreakableCrateInstantBreakDamage;
        }

        return damageKind switch
        {
            "Melee" => BreakableCrateCrowbarDamage,
            "Bullet" => BreakableCrateBulletDamage,
            _ => amount
        };
    }

    private bool TryBreakFractureCrateEntity(Entity entity, bool persist, bool showMessage)
    {
        if (!UsesBreakableCrateFractureVisuals(entity))
            return false;

        if (ReferenceEquals(_held, entity))
            DropHeld();

        SpawnFractureDebris(entity);
        TrySpawnCrateBreakPickupReward(entity);

        entity.IsBroken = true;
        entity.Damageable = false;
        entity.Health = 0f;
        entity.BrokenReplacementModelPath = "";
        entity.FractureVisualActive = false;
        entity.HiddenModelPartKeys.Clear();
        entity.CanPickUp = false;
        entity.Physics.MotionType = MotionType.Static;
        entity.Physics.BoxBody = null;
        entity.Physics.SphereBody = null;
        entity.Physics.CapsuleBody = null;
        HideRuntimeEntity(entity);

        if (persist && !string.IsNullOrWhiteSpace(entity.Name))
            _brokenObjectReplacementModels[entity.Name] = "";

        if (showMessage)
            ShowGameMessage($"{PrettifyToken(entity.Name)} broke.", 1.1f);

        return true;
    }

    private void TrySpawnCrateBreakPickupReward(Entity source)
    {
        if (!TryChooseCrateBreakPickupReward(out string itemId))
            return;

        Vector3 randomOffset = new(
            (Random.Shared.NextSingle() - 0.5f) * 0.65f,
            MathF.Max(0.35f, source.Render.Size.Y * 0.55f),
            (Random.Shared.NextSingle() - 0.5f) * 0.65f);
        Vector3 position = source.Transform.Position + randomOffset;
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(Random.Shared.NextSingle() * MathF.PI * 2f, 0f, 0f);
        SpawnWorldItemAt(itemId, 1, position, rotation);
    }

    private bool TryChooseCrateBreakPickupReward(out string itemId)
    {
        itemId = "";
        float dropChance = CrateRewardBaseDropChance;
        if (_playerHealth < 25)
            dropChance += CrateRewardLowHealthDropBonus;
        if (_playerSuit < 25)
            dropChance += CrateRewardLowSuitDropBonus;
        dropChance = Math.Clamp(dropChance, CrateRewardBaseDropChance, 0.65f);

        if (Random.Shared.NextSingle() >= dropChance)
            return false;

        float missingHealth = Math.Clamp(100 - _playerHealth, 0, 100) / 100f;
        float missingSuit = Math.Clamp(100 - _playerSuit, 0, 100) / 100f;
        float healthWeight = 0.5f + missingHealth * 1.5f;
        float suitWeight = 0.5f + missingSuit * 1.25f;

        if (_playerHealth < 25)
            healthWeight += 4.0f;
        else if (_playerHealth < 50)
            healthWeight += 1.0f;

        if (_playerSuit < 25)
            suitWeight += 3.0f;
        else if (_playerSuit < 50)
            suitWeight += 1.0f;

        if (_playerHealth >= 100)
            healthWeight *= 0.25f;
        if (_playerSuit >= 100)
            suitWeight *= 0.25f;

        itemId = Random.Shared.NextSingle() * (healthWeight + suitWeight) < healthWeight
            ? ItemCatalog.HealthPack
            : ItemCatalog.SuitBattery;
        return true;
    }
    private IReadOnlySet<string>? BuildHiddenModelPartKeys(Entity entity, LoadedModel? model)
    {
        if (!UsesBreakableCrateFractureVisuals(entity) || model == null)
            return null;

        HashSet<string> hidden = new(StringComparer.OrdinalIgnoreCase);
        foreach (LoadedModelPart part in model.Parts)
        {
            if (IsBreakableCrateFracturePart(part))
            {
                if (!entity.FractureVisualActive || entity.HiddenModelPartKeys.Contains(part.PartKey))
                    hidden.Add(part.PartKey);
            }
            else if (entity.FractureVisualActive && IsBreakableCrateIntactPart(part))
            {
                hidden.Add(part.PartKey);
            }
        }

        return hidden.Count > 0 ? hidden : null;
    }

    private void UpdateFractureDebris(float dt)
    {
        if (_fractureDebrisPieces.Count == 0)
            return;

        dt = Math.Clamp(dt, 0f, 0.05f);
        for (int i = _fractureDebrisPieces.Count - 1; i >= 0; i--)
        {
            FractureDebrisPiece piece = _fractureDebrisPieces[i];
            piece.Age += dt;
            if (piece.Age >= piece.Lifetime)
            {
                _fractureDebrisPieces.RemoveAt(i);
                continue;
            }

            piece.Velocity += new Vector3(0f, -FractureDebrisGravity * dt, 0f);
            piece.Position += piece.Velocity * dt;

            if (piece.Position.Y <= piece.GroundCenterY)
            {
                piece.Position = new Vector3(piece.Position.X, piece.GroundCenterY, piece.Position.Z);
                piece.Velocity = new Vector3(piece.Velocity.X * 0.35f, 0f, piece.Velocity.Z * 0.35f);
                piece.AngularVelocity *= 0.45f;
            }

            if (piece.AngularVelocity.LengthSquared() > 0.000001f)
            {
                Quaternion delta = Quaternion.CreateFromYawPitchRoll(
                    piece.AngularVelocity.Y * dt,
                    piece.AngularVelocity.X * dt,
                    piece.AngularVelocity.Z * dt);
                piece.Rotation = Quaternion.Normalize(delta * piece.Rotation);
            }
        }
    }

    private void DrawFractureDebris(Renderer renderer)
    {
        if (_fractureDebrisPieces.Count == 0)
            return;

        for (int i = 0; i < _fractureDebrisPieces.Count; i++)
        {
            FractureDebrisPiece piece = _fractureDebrisPieces[i];
            float alpha = GetFractureDebrisAlpha(piece);
            if (alpha <= 0.01f)
                continue;

            if (!TryGetReadyModel(piece.ModelAssetPath, "fracture debris", out WeaponModelCacheEntry? entry) || entry?.Model == null)
                continue;

            Matrix4x4 transform = CreateModelPartCenterTransform(entry.Bounds, piece.LocalCenter, piece.Position, piece.Size, piece.Rotation);
            _world.DrawModel(renderer.CommandList, entry.Model, transform, new Vector4(1f, 1f, 1f, alpha), piece.HiddenPartKeys);
        }
    }

    private static float GetFractureDebrisAlpha(FractureDebrisPiece piece)
    {
        if (piece.Age <= FractureDebrisFadeStartSeconds)
            return 1f;

        float fadeDuration = MathF.Max(0.001f, piece.Lifetime - FractureDebrisFadeStartSeconds);
        return Math.Clamp((piece.Lifetime - piece.Age) / fadeDuration, 0f, 1f);
    }

    private void SpawnFractureDebris(Entity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.Render.ModelAssetPath) ||
            !TryGetReadyModel(entity.Render.ModelAssetPath, "fracture debris", out WeaponModelCacheEntry? entry) ||
            entry?.LoadedModel == null)
        {
            return;
        }

        IReadOnlyList<LoadedModelPart> parts = entry.LoadedModel.Parts;
        Quaternion baseRotation = GetColliderRotation(entity);
        Matrix4x4 rotationMatrix = Matrix4x4.CreateFromQuaternion(baseRotation);
        Matrix4x4 baseTransform = CreateBoundsFitTransform(entry.Bounds, entity.Transform.Position, entity.Render.Size, baseRotation);
        Vector3 modelCenter = entry.Bounds.Valid ? entry.Bounds.Center : Vector3.Zero;
        Vector3 modelScale = GetBoundsFitScale(entry.Bounds, entity.Render.Size);

        for (int i = 0; i < parts.Count; i++)
        {
            LoadedModelPart part = parts[i];
            if (!IsBreakableCrateFracturePart(part) || entity.HiddenModelPartKeys.Contains(part.PartKey))
                continue;

            GetModelPartLocalBounds(part, out Vector3 partMin, out Vector3 partMax);
            Vector3 localCenter = (partMin + partMax) * 0.5f;
            Vector3 worldCenter = Vector3.Transform(localCenter, baseTransform);
            float worldHalfHeight = MathF.Max(0.025f, MathF.Abs(partMax.Y - partMin.Y) * MathF.Abs(modelScale.Y) * 0.5f);

            FractureDebrisPiece piece = new()
            {
                ModelAssetPath = entity.Render.ModelAssetPath,
                Position = worldCenter,
                LocalCenter = localCenter,
                Size = entity.Render.Size,
                Rotation = baseRotation,
                Velocity = BuildFractureDebrisVelocity(part, modelCenter, rotationMatrix),
                AngularVelocity = BuildFractureDebrisAngularVelocity(),
                GroundCenterY = FindFractureDebrisGroundCenterY(entity, worldCenter, worldHalfHeight),
                Lifetime = FractureDebrisLifetimeSeconds
            };

            for (int j = 0; j < parts.Count; j++)
            {
                if (!string.Equals(parts[j].PartKey, part.PartKey, StringComparison.OrdinalIgnoreCase))
                    piece.HiddenPartKeys.Add(parts[j].PartKey);
            }

            _fractureDebrisPieces.Add(piece);
        }
    }

    private float FindFractureDebrisGroundCenterY(Entity source, Vector3 worldCenter, float worldHalfHeight)
    {
        float bestTopY = 0f;
        bool foundSupport = false;
        const float horizontalPadding = 0.08f;

        for (int i = 0; i < _runtimeEntities.Count; i++)
        {
            Entity candidate = _runtimeEntities[i];
            if (ReferenceEquals(candidate, source) || !candidate.Collider.Enabled || !candidate.Collider.IsSolid)
                continue;

            Engine.Physics.Collision.Aabb aabb = CreateWorldCollider(candidate).GetAabb();
            if (worldCenter.X < aabb.Min.X - horizontalPadding || worldCenter.X > aabb.Max.X + horizontalPadding ||
                worldCenter.Z < aabb.Min.Z - horizontalPadding || worldCenter.Z > aabb.Max.Z + horizontalPadding)
            {
                continue;
            }

            if (aabb.Max.Y > worldCenter.Y + worldHalfHeight + 0.05f)
                continue;

            if (!foundSupport || aabb.Max.Y > bestTopY)
            {
                bestTopY = aabb.Max.Y;
                foundSupport = true;
            }
        }

        return bestTopY + worldHalfHeight;
    }

    private static Matrix4x4 CreateModelPartCenterTransform(ModelBounds bounds, Vector3 partLocalCenter, Vector3 position, Vector3 size, Quaternion rotation)
        => Matrix4x4.CreateTranslation(-partLocalCenter) *
           Matrix4x4.CreateScale(GetBoundsFitScale(bounds, size)) *
           Matrix4x4.CreateFromQuaternion(rotation) *
           Matrix4x4.CreateTranslation(position);

    private static Vector3 GetBoundsFitScale(ModelBounds bounds, Vector3 size)
    {
        if (!bounds.Valid)
            return size;

        Vector3 boundsSize = new(
            MathF.Max(0.0001f, bounds.Size.X),
            MathF.Max(0.0001f, bounds.Size.Y),
            MathF.Max(0.0001f, bounds.Size.Z));
        return new Vector3(
            MathF.Abs(size.X) / boundsSize.X,
            MathF.Abs(size.Y) / boundsSize.Y,
            MathF.Abs(size.Z) / boundsSize.Z);
    }

    private static Vector3 BuildFractureDebrisVelocity(LoadedModelPart part, Vector3 modelCenter, Matrix4x4 rotationMatrix)
    {
        Vector3 localOut = GetModelPartLocalCenter(part) - modelCenter;
        if (localOut.LengthSquared() < 0.0001f)
            localOut = new Vector3(Random.Shared.NextSingle() - 0.5f, 0.2f, Random.Shared.NextSingle() - 0.5f);

        localOut = Vector3.Normalize(localOut);
        Vector3 worldOut = Vector3.TransformNormal(localOut, rotationMatrix);
        if (worldOut.LengthSquared() < 0.0001f)
            worldOut = Vector3.UnitY;
        else
            worldOut = Vector3.Normalize(worldOut);

        float horizontalSpeed = 0.25f + Random.Shared.NextSingle() * 0.75f;
        float lift = 0.15f + Random.Shared.NextSingle() * 0.65f;
        return new Vector3(worldOut.X, 0f, worldOut.Z) * horizontalSpeed + new Vector3(0f, lift, 0f);
    }

    private static Vector3 BuildFractureDebrisAngularVelocity()
        => new(
            -1.6f + Random.Shared.NextSingle() * 3.2f,
            -2.4f + Random.Shared.NextSingle() * 4.8f,
            -1.6f + Random.Shared.NextSingle() * 3.2f);

    private static Vector3 GetModelPartLocalCenter(LoadedModelPart part)
    {
        GetModelPartLocalBounds(part, out Vector3 min, out Vector3 max);
        return (min + max) * 0.5f;
    }

    private static void GetModelPartLocalBounds(LoadedModelPart part, out Vector3 min, out Vector3 max)
    {
        if (part.Positions.Length == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
            return;
        }

        min = new Vector3(float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity);
        for (int i = 0; i < part.Positions.Length; i++)
        {
            min = Vector3.Min(min, part.Positions[i]);
            max = Vector3.Max(max, part.Positions[i]);
        }
    }

    private bool UsesBreakableCrateFractureVisuals(Entity entity)
        => IsBreakableWoodenCrateModelPath(entity.Render.ModelAssetPath) &&
           entity.Render.Enabled &&
           entity.Render.Shape != RuntimeShapeKind.None;

    private static bool IsBreakableWoodenCrateModelPath(string? modelAssetPath)
    {
        if (string.IsNullOrWhiteSpace(modelAssetPath))
            return false;

        string normalized = modelAssetPath.Replace('\\', '/');
        return normalized.EndsWith(BreakableWoodenCrateModelPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBreakableCrateFracturePart(LoadedModelPart part)
        => part.NodeName.Contains(BreakableCrateFractureToken, StringComparison.OrdinalIgnoreCase) ||
           part.MeshName.Contains(BreakableCrateFractureToken, StringComparison.OrdinalIgnoreCase);

    private static bool IsBreakableCrateIntactPart(LoadedModelPart part)
        => string.Equals(part.NodeName, BreakableCrateIntactNodeName, StringComparison.OrdinalIgnoreCase);

    private bool TryFindNearestVisibleFracturePart(
        Entity entity,
        LoadedModel model,
        ModelBounds bounds,
        Vector3 hitPoint,
        out string partKey)
    {
        partKey = "";
        Matrix4x4 modelTransform = CreateBoundsFitTransform(bounds, entity.Transform.Position, entity.Render.Size, GetColliderRotation(entity));
        float bestDistanceSq = float.PositiveInfinity;

        foreach (LoadedModelPart part in model.Parts)
        {
            if (!IsBreakableCrateFracturePart(part) || entity.HiddenModelPartKeys.Contains(part.PartKey))
                continue;

            float distanceSq = DistanceSquaredToModelPart(part, hitPoint, modelTransform);
            if (distanceSq >= bestDistanceSq)
                continue;

            bestDistanceSq = distanceSq;
            partKey = part.PartKey;
        }

        return !string.IsNullOrWhiteSpace(partKey);
    }

    private static float DistanceSquaredToModelPart(LoadedModelPart part, Vector3 hitPoint, Matrix4x4 modelTransform)
    {
        float best = float.PositiveInfinity;
        Vector3[] positions = part.Positions;
        uint[] indices = part.Indices;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int ia = (int)indices[i];
            int ib = (int)indices[i + 1];
            int ic = (int)indices[i + 2];
            if ((uint)ia >= positions.Length || (uint)ib >= positions.Length || (uint)ic >= positions.Length)
                continue;

            Vector3 a = Vector3.Transform(positions[ia], modelTransform);
            Vector3 b = Vector3.Transform(positions[ib], modelTransform);
            Vector3 c = Vector3.Transform(positions[ic], modelTransform);
            Vector3 center = (a + b + c) / 3f;

            best = MathF.Min(best, Vector3.DistanceSquared(center, hitPoint));
            best = MathF.Min(best, Vector3.DistanceSquared(a, hitPoint));
            best = MathF.Min(best, Vector3.DistanceSquared(b, hitPoint));
            best = MathF.Min(best, Vector3.DistanceSquared(c, hitPoint));
        }

        if (float.IsPositiveInfinity(best))
        {
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 world = Vector3.Transform(positions[i], modelTransform);
                best = MathF.Min(best, Vector3.DistanceSquared(world, hitPoint));
            }
        }

        return best;
    }
}