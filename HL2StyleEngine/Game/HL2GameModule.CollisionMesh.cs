using Engine.Editor.Level;
using Engine.Physics.Collision;
using Engine.Render;
using System.Numerics;

namespace Game;

public sealed partial class HL2GameModule
{
    private bool TryCreateMeshCollider(LevelEntityDef def, Vector3 position, Vector3 size, Quaternion rotation, out MeshCollisionMesh mesh)
    {
        mesh = null!;

        if (!IsGlbModelPath(def.MeshPath) ||
            !TryGetLoadedModelForCollision(def.MeshPath, out LoadedModel model, out ModelBounds bounds))
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
        return mesh.IsValid;
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

        if (entry.LoadedModel == null && entry.LoadTask is { } task)
        {
            try
            {
                LoadedModel loaded = task.GetAwaiter().GetResult();
                entry.LoadedModel = loaded;
                entry.Bounds = CalculateModelBounds(loaded);
                entry.Model = _world.CreateRenderModel(loaded);
                entry.LoadTask = null;
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




