using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;
using Engine.Runtime.Entities;
using Veldrid;

namespace Game;

public sealed partial class HL2GameModule
{
    private bool _flashlightOn;
    private double _editorLightingSeconds;
    private readonly LevelLightState _lightState = new();
    private readonly List<ShadowCaster> _shadowCasters = new();

    private WorldPointLight CreateFlashlight()
    {
        Vector3 origin = _camera.Position, direction = _camera.Forward;
        if (_editor.LevelFile.OffsetFlashlight)
        {
            Vector3 right = _camera.Right;
            Vector3 viewUp = Vector3.Normalize(Vector3.Cross(right, direction));
            Vector3 offset = right * .04f - viewUp * .02f;
            float distance = offset.Length();
            const float wallClearance = .005f;
            Vector3 rayDirection = offset / distance;
            var ray = new Engine.Physics.Collision.Ray(origin,rayDirection);
            foreach (var entity in _runtimeEntities)
            {
                if (!entity.Collider.Enabled || !entity.Collider.IsSolid ||
                    entity.Type is EntityTypes.TriggerVolume or EntityTypes.PointLight or EntityTypes.PlayerSpawn) continue;
                // Include the clearance in the query so the source retracts continuously at the boundary.
                float reach = distance + wallClearance;
                if (!Engine.Physics.Collision.Raycast.RayIntersectsAabb(ray,CreateWorldCollider(entity).GetAabb(),0,reach,out _)) continue;
                if (RayIntersectsEntityCollider(ray,entity,0,reach,out float hit)) distance = MathF.Min(distance,MathF.Max(0,hit-wallClearance));
            }
            origin += rayDirection * distance;
            direction = Vector3.Normalize(_camera.Position + _camera.Forward * 8 - origin);
        }
        return new(origin,new Vector3(1f,.95f,.85f),7,22,direction,55,true,100,"player-flashlight");
    }

    private bool IsLightSwitch(Entity entity) => IsInteractionKind(entity, "LightSwitch");

    private string GetLightSwitchPrompt(Entity entity)
    {
        var interaction = GetInteraction(entity)!;
        if (!AreInteractionRequiredStatesComplete(entity))
            return string.IsNullOrWhiteSpace(interaction.LockedPrompt) ? "Power unavailable" : interaction.LockedPrompt;
        var lights = LevelLightState.Targets(_editor.LevelFile, interaction).ToArray();
        if (lights.Length == 0) return "No lights connected";
        if (!string.IsNullOrWhiteSpace(interaction.Prompt)) return interaction.Prompt;
        return lights.Any(_lightState.IsEnabled) ? "Switch lights off" : "Switch lights on";
    }

    private void UseLightSwitch(Entity entity)
    {
        var interaction = GetInteraction(entity)!;
        if (!AreInteractionRequiredStatesComplete(entity))
        {
            ShowGameMessage(GetLightSwitchPrompt(entity));
            return;
        }
        if (!_lightState.Toggle(_editor.LevelFile, interaction, out bool enabled))
        {
            ShowGameMessage("No lights connected.");
            return;
        }
        ShowGameMessage(string.IsNullOrWhiteSpace(interaction.SuccessMessage)
            ? enabled ? "Lights on." : "Lights off."
            : interaction.SuccessMessage);
    }

    private void DrawWorldShadows(Renderer renderer)
    {
        _shadowCasters.Clear();
        if (_world.NeedsShadowCasters)
        {
            if (_editorEnabled)
            {
                for (int i = 0; i < _editor.DrawBoxes.Count; i++)
                {
                    var def = _editor.LevelFile.Entities[i];
                    if (def.Type is EntityTypes.PointLight or EntityTypes.PlayerSpawn or EntityTypes.TriggerVolume or EntityTypes.ReflectionProbe) continue;
                    var draw = _editor.DrawBoxes[i];
                    Add(def.MeshPath, draw.Position, draw.Size, draw.Rotation, draw.Color, null);
                }
            }
            else if (RenderPreparationEnabled)
            {
                _shadowCasters.AddRange(_preparedShadows);
            }
            else
            {
                foreach (var entity in _runtimeEntities)
                {
                    if (!entity.Render.Enabled) continue;
                    Add(entity.Render.ModelAssetPath, entity.Transform.Position, entity.Render.Size,
                        GetColliderRotation(entity), entity.Render.Color, entity);
                }
            }
        }
        _world.RenderShadows(renderer, _shadowCasters, new Viewport(0, 0, _ctx.Window.Window.Width, _ctx.Window.Window.Height, 0, 1));

        void Add(string? path, Vector3 position, Vector3 size, Quaternion rotation, Vector4 color, Entity? entity)
        {
            if (!_world.IsShadowRelevant(position, size.Length() * .5f)) return;
            RenderModel? model = null;
            IReadOnlySet<string>? hidden = null;
            Matrix4x4 transform = Matrix4x4.CreateScale(size) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
            if (!string.IsNullOrWhiteSpace(path) && TryGetReadyModel(path, "shadow caster", out var entry) && entry?.Model != null)
            {
                model = entry.Model;
                transform = CreateBoundsFitTransform(entry.Bounds, position, size, rotation);
                if (entity != null) hidden = BuildHiddenModelPartKeys(entity, entry.LoadedModel);
            }
            if (model == null && color.W <= .01f) return;
            _shadowCasters.Add(new ShadowCaster(model, transform, position, size.Length() * .5f, hidden));
        }
    }
}
