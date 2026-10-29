using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Render;
using System.Numerics;
using System.Security.Cryptography;
using Engine.Core.Assets;

namespace Game;

public sealed partial class HL2GameModule
{
    private readonly Dictionary<string, byte[]> _collisionSourceHashes = new(StringComparer.OrdinalIgnoreCase);
    public double CollisionPreparationMilliseconds { get; private set; }
    public int CollisionCacheHits { get; private set; }
    public int CollisionMeshesBuilt { get; private set; }

    private bool TryCreateMeshCollider(LevelEntityDef def, Vector3 position, Vector3 size, Quaternion rotation,
        out MeshCollisionMesh mesh, bool cacheStaticPose = true)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return CreateMeshCollider(def, position, size, rotation, out mesh, cacheStaticPose); }
        finally { CollisionPreparationMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    private bool CreateMeshCollider(LevelEntityDef def, Vector3 position, Vector3 size, Quaternion rotation,
        out MeshCollisionMesh mesh, bool cacheStaticPose)
    {
        mesh = null!;
        if (!IsGlbModelPath(def.MeshPath)) return false;
        byte[]? key = null;
        string? directory = null;
        if (cacheStaticPose && MeshCollisionCache.Enabled)
        {
            try
            {
                string path = ResolveModelAssetPath(def.MeshPath);
                if (!_collisionSourceHashes.TryGetValue(path, out var hash))
                {
                    _collisionSourceHashes[path] = hash = AssetFingerprintReader.ReadHash(path, "collision-dependency", _strictFingerprints);
                }
                key = CreateCollisionCacheKey(hash, position, size, rotation);
                directory = Path.Combine(Path.GetDirectoryName(TextureCooker.CacheDirectory(path))!, "collision");
                if (MeshCollisionCache.TryLoad(directory, key, out mesh))
                {
                    CollisionCacheHits++;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { key = null; directory = null; }
        }

        if (!TryGetLoadedModelForCollision(def.MeshPath, out LoadedModel model, out ModelBounds bounds))
        {
            return false;
        }
        Matrix4x4 transform = CreateBoundsFitTransform(bounds, position, size, rotation);

        List<MeshCollisionTriangle> triangles = new();
        foreach (LoadedModelPart part in model.Parts)
        {
            Vector3[] positions = part.Positions;
            uint[] indices = part.Indices;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                uint ia = indices[i];
                uint ib = indices[i + 1];
                uint ic = indices[i + 2];
                if (ia >= positions.Length || ib >= positions.Length || ic >= positions.Length)
                    continue;

                Vector3 a = Vector3.Transform(positions[(int)ia], transform);
                Vector3 b = Vector3.Transform(positions[(int)ib], transform);
                Vector3 c = Vector3.Transform(positions[(int)ic], transform);

                if (Vector3.Cross(b - a, c - a).LengthSquared() <= 1e-10f)
                    continue;

                triangles.Add(new MeshCollisionTriangle(a, b, c));
            }
        }

        if (triangles.Count <= 0)
            return false;

        mesh = new MeshCollisionMesh(triangles);
        CollisionMeshesBuilt++;
        if (key != null && directory != null) MeshCollisionCache.TryStore(directory, key, mesh);
        return mesh.IsValid;
    }

    private static byte[] CreateCollisionCacheKey(byte[] sourceHash, Vector3 position, Vector3 size, Quaternion rotation)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(sourceHash);
            writer.Write(MeshCollisionCache.Version);
            // Importer and bounds-fit changes invalidate existing cooked placements on rebuild.
            writer.Write(typeof(GlbModelLoader).Module.ModuleVersionId.ToByteArray());
            writer.Write(typeof(HL2GameModule).Module.ModuleVersionId.ToByteArray());
            writer.Write(position.X); writer.Write(position.Y); writer.Write(position.Z);
            writer.Write(size.X); writer.Write(size.Y); writer.Write(size.Z);
            writer.Write(rotation.X); writer.Write(rotation.Y); writer.Write(rotation.Z); writer.Write(rotation.W);
            writer.Write(MeshCollisionMesh.DefaultCollisionSkin);
        }
        return SHA256.HashData(stream.ToArray());
    }

    private bool TryGetLoadedModelForCollision(string modelAssetPath, out LoadedModel model, out ModelBounds bounds)
    {
        model = null!;
        bounds = default;

        if (string.IsNullOrWhiteSpace(modelAssetPath))
            return false;

        string path = ResolveModelAssetPath(modelAssetPath);
        if (!_weaponModelCache.TryGetValue(path, out WeaponModelCacheEntry? entry))
        {
            PreloadModelAsset(modelAssetPath);
            _weaponModelCache.TryGetValue(path, out entry);
        }

        if (entry == null)
            return false;

        StartModelLoad(path, entry);

        if (entry.LoadedModel == null && entry.LoadTask is { } task)
        {
            try
            {
                LoadedModel loaded = task.GetAwaiter().GetResult();
                entry.LoadedModel = loaded;
                entry.Bounds = CalculateModelBounds(loaded);
                // Collision only needs CPU geometry. GPU work is budgeted during rendering.
            }
            catch (Exception ex)
            {
                entry.Failed = true;
                entry.Error = ex.Message;
                entry.LoadTask = null;
                return false;
            }
        }

        if (entry.LoadedModel == null || entry.Failed)
            return false;

        model = entry.LoadedModel;
        bounds = entry.Bounds.Valid ? entry.Bounds : CalculateModelBounds(model);
        return bounds.Valid;
    }
}




