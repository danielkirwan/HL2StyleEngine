using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public static class LevelLighting
{
    public static ModelEmission GetEmission(LevelFile level, LevelEntityDef? entity,
        LevelLightState? state = null, double seconds = 0)
    {
        if (entity == null) return new ModelEmission(1);
        float strength = float.IsFinite(entity.EmissionIntensity) ? Math.Clamp(entity.EmissionIntensity, 0, 100) : 0;
        if (!string.IsNullOrWhiteSpace(entity.EmissionLightId))
        {
            var light = level.Entities.Find(e => e.Type == EntityTypes.PointLight &&
                string.Equals(e.Id, entity.EmissionLightId, StringComparison.OrdinalIgnoreCase));
            // A missing/deleted link fails dark, not permanently on.
            strength *= light != null && level.UsePointLights && (state?.IsEnabled(light) ?? light.LightEnabled) &&
                float.IsFinite(light.Intensity) && light.Intensity > 0 && float.IsFinite(light.Range) && light.Range > 0
                ? LevelLightState.Flicker(light, seconds) : 0;
        }
        return new ModelEmission(strength, entity.EmissionOverride ? (Vector3)entity.EmissionColor : null, entity.EmissionPart);
    }

    public static void Apply(BasicWorldRenderer renderer, LevelEditorController editor, Vector3 cameraPosition,
        WorldPointLight? flashlight = null, LevelLightState? state = null, double seconds = 0)
    {
        renderer.AmbientStrength = editor.LevelFile.AmbientLight;
        renderer.DirectionalStrength = editor.LevelFile.DirectionalLight;
        renderer.ShadowsEnabled = editor.LevelFile.EnableShadows;
        renderer.PointShadowHysteresis = editor.LevelFile.PointShadowHysteresis;
        renderer.LightingModel = editor.LevelFile.LightingModel == SurfaceLightingModel.Pbr ? SurfaceLightingModel.Pbr : SurfaceLightingModel.Legacy;
        renderer.EnvironmentPreset = editor.LevelFile.ReflectionEnvironment;
        var lights = GetLights(editor, state, seconds);
        renderer.UpdatePointLights(flashlight is { } light ? lights.Prepend(light) : lights, cameraPosition);
        editor.ReflectionStatus = renderer.ReflectionStatus;
    }

    public static IEnumerable<WorldPointLight> GetLights(LevelEditorController editor, LevelLightState? state = null, double seconds = 0)
    {
        if (!editor.LevelFile.UsePointLights)
            yield break;

        for (int i = 0; i < editor.LevelFile.Entities.Count; i++)
        {
            var entity = editor.LevelFile.Entities[i];
            if (entity.Type != EntityTypes.PointLight || !(state?.IsEnabled(entity) ?? entity.LightEnabled) ||
                !editor.TryGetEntityWorldTRS(i, out var position, out var rotation, out _))
                continue;

            Vector4 color = entity.LightColor;
            yield return new WorldPointLight(position, new Vector3(color.X, color.Y, color.Z), entity.Intensity * LevelLightState.Flicker(entity, seconds), entity.Range,
                Vector3.Transform(-Vector3.UnitZ, rotation), entity.IsSpotLight ? entity.SpotAngleDeg : 0, entity.CastShadows);
        }
    }
}
