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
                    if (def.Type == EntityTypes.PointLight || def.Type == EntityTypes.PlayerSpawn || def.Type == EntityTypes.TriggerVolume) continue;
                    var draw = _editor.DrawBoxes[i];
                    Add(def.MeshPath, draw.Position, draw.Size, draw.Rotation, draw.Color, null);
                }
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
