using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public static class LevelLighting
{
    public static void Apply(BasicWorldRenderer renderer, LevelEditorController editor, Vector3 cameraPosition,
        WorldPointLight? flashlight = null, LevelLightState? state = null, double seconds = 0)
    {
        renderer.AmbientStrength = editor.LevelFile.AmbientLight;
        renderer.DirectionalStrength = editor.LevelFile.DirectionalLight;
        renderer.ShadowsEnabled = editor.LevelFile.EnableShadows;
        var lights = GetLights(editor, state, seconds);
        renderer.UpdatePointLights(flashlight is { } light ? lights.Prepend(light) : lights, cameraPosition);
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
