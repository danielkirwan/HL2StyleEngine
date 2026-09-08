using System.Numerics;
using Engine.Editor.Level;
using Engine.Render;

namespace Engine.Editor.Editor;

public static class LevelLighting
{
    public static void Apply(BasicWorldRenderer renderer, LevelEditorController editor, Vector3 cameraPosition)
        => renderer.UpdatePointLights(GetLights(editor), cameraPosition);

    private static IEnumerable<WorldPointLight> GetLights(LevelEditorController editor)
    {
        if (!editor.LevelFile.UsePointLights)
            yield break;

        for (int i = 0; i < editor.LevelFile.Entities.Count; i++)
        {
            var entity = editor.LevelFile.Entities[i];
            if (entity.Type != EntityTypes.PointLight ||
                !editor.TryGetEntityWorldTRS(i, out var position, out _, out _))
                continue;

            Vector4 color = entity.LightColor;
            yield return new WorldPointLight(position, new Vector3(color.X, color.Y, color.Z), entity.Intensity, entity.Range);
        }
    }
}
