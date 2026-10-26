using System.Diagnostics;
using System.Numerics;
using Engine.Editor.Editor;
using Engine.Physics.Dynamics;
using Engine.Render;
using Engine.Runtime.Entities;

namespace Game;

public sealed partial class HL2GameModule
{
    public bool RenderPreparationEnabled { get; set; } = true;
    public double RenderPreparationMilliseconds { get; private set; }
    public int PreparedModels => _renderScene.Count;
    private readonly RenderScene _renderScene = new();
    private readonly List<PreparedDraw> _preparedDraws = new();
    private readonly List<ShadowCaster> _preparedShadows = new();
    private readonly record struct PreparedDraw(int ModelIndex, RuntimeShapeKind Shape, Vector3 Position,
        Vector3 Size, Quaternion Rotation, Vector4 Color, float Radius, float Height);

    private void PrepareRenderScene(Renderer renderer)
    {
        long start = Stopwatch.GetTimestamp();
        _renderScene.Begin(); _preparedDraws.Clear(); _preparedShadows.Clear();
        foreach (var entity in _runtimeEntities)
        {
            if (!entity.Render.Enabled) continue;
            RenderBounds(entity, out var min, out var max);
            bool inRoom = entity.IsHeld || RoomCanSee(min, max);
            if (!inRoom) RoomCulledEntities++;
            bool visible = inRoom && _world.IntersectsView(min, max, Matrix4x4.Identity);
            var position = entity.Transform.Position; var size = entity.Render.Size;
            float radius = size.Length() * .5f;
            bool shadow = _world.NeedsShadowCasters && _world.IsShadowRelevant(position, radius);
            if (!visible && !shadow) continue;
            var rotation = GetColliderRotation(entity); var tint = entity.Render.Color;
            bool hasModel = !string.IsNullOrWhiteSpace(entity.Render.ModelAssetPath);
            WeaponModelCacheEntry? entry = null;
            bool ready = hasModel && TryGetReadyModel(entity.Render.ModelAssetPath, "render scene", out entry) && entry?.Model != null;
            if (ready)
            {
                var transform = CreateBoundsFitTransform(entry!.Bounds, position, size, rotation);
                var hidden = BuildHiddenModelPartKeys(entity, entry.LoadedModel);
                var def = FindLevelEntityDef(entity);
                var instance = new RenderInstance(def?.Id ?? "", entry.Model!, transform, tint, position, radius, hidden,
                    visible ? LevelLighting.GetEmission(_editor.LevelFile, def, _lightState, _playTimeSeconds) : null,
                    visible ? _bakedLighting.GetBinding(def, entry.LoadedModel!, transform, _editor.LevelFile, _world, renderer, _lightState) : null,
                    visible ? _bakedLighting.SampleProbe(entity, position, def) : default, visible, shadow,
                    def is { MotionType: MotionType.Static, CanPickUp: false, Damageable: false, Interaction: null } &&
                        def.Scripts.Count == 0 && !entity.IsHeld && !entity.Damageable);
                int index = _renderScene.Count; _renderScene.Add(instance);
                if (shadow) _preparedShadows.Add(instance.Shadow);
                if (visible) _preparedDraws.Add(new(index, default, default, default, default, default, 0, 0));
            }
            else
            {
                if (shadow && tint.W > .01f)
                    _preparedShadows.Add(new(null, Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation) *
                        Matrix4x4.CreateTranslation(position), position, radius));
                bool pending = hasModel && AssetStreamingEnabled && entry is { Failed: false };
                if (visible && !pending && (hasModel || tint.W > .01f))
                    _preparedDraws.Add(new(-1, entity.Render.Shape, position, size, rotation, tint, entity.Render.Radius, entity.Render.Height));
            }
        }
        _renderScene.Seal();
        RenderPreparationMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void DrawPreparedScene(Renderer renderer)
    {
        using var group = _world.GroupModelDraws(renderer.CommandList);
        foreach (var draw in _preparedDraws)
        {
            if (draw.ModelIndex >= 0) _world.DrawInstance(renderer.CommandList, _renderScene.Instances[draw.ModelIndex]);
            else DrawPrimitive(renderer, draw.Shape, draw.Position, draw.Size, draw.Rotation, draw.Color, draw.Radius, draw.Height);
        }
    }
}
