using System;
using System.Numerics;
using ImGuiNET;

namespace Game;

public static class ImGui3DGrid
{
    public static void DrawGroundGrid(
        Vector2 viewportSize,
        Vector3 cameraPos,
        Vector3 cameraForward,
        float fovRadians,
        float aspect,
        float nearPlane,
        float farPlane,
        int halfSize = 30,
        float spacing = 1f)
    {
        // Build camera matrices
        var view = Matrix4x4.CreateLookAt(cameraPos, cameraPos + cameraForward, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovRadians, aspect, nearPlane, farPlane);

        // NOTE: For our projection helper we use ViewProj = View * Proj (row-vector style in System.Numerics usage here)
        var viewProj = view * proj;

        var draw = ImGui.GetBackgroundDrawList();

        // Colors (packed ABGR for ImGui)
        uint gridColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.35f, 0.35f, 1f));
        uint xAxisColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 1f));
        uint zAxisColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.40f, 1.00f, 1f));

        float w = viewportSize.X;
        float h = viewportSize.Y;

        // Draw lines parallel to X (vary Z)
        for (int z = -halfSize; z <= halfSize; z++)
        {
            float zz = z * spacing;
            uint c = (z == 0) ? zAxisColor : gridColor;

            Vector3 a = new Vector3(-halfSize * spacing, 0, zz);
            Vector3 b = new Vector3(+halfSize * spacing, 0, zz);

            if (TryProject(viewProj, a, w, h, out var pa) && TryProject(viewProj, b, w, h, out var pb))
                draw.AddLine(pa, pb, c, 1.0f);
        }

        // Draw lines parallel to Z (vary X)
        for (int x = -halfSize; x <= halfSize; x++)
        {
            float xx = x * spacing;
            uint c = (x == 0) ? xAxisColor : gridColor;

            Vector3 a = new Vector3(xx, 0, -halfSize * spacing);
            Vector3 b = new Vector3(xx, 0, +halfSize * spacing);

            if (TryProject(viewProj, a, w, h, out var pa) && TryProject(viewProj, b, w, h, out var pb))
                draw.AddLine(pa, pb, c, 1.0f);
        }
    }

    private static bool TryProject(Matrix4x4 viewProj, Vector3 world, float w, float h, out Vector2 screen)
    {
        // Transform to clip space
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProj);

        // Behind camera or too close to w=0
        if (clip.W <= 0.0001f)
        {
            screen = default;
            return false;
        }

        // NDC
        float invW = 1f / clip.W;
        float ndcX = clip.X * invW;
        float ndcY = clip.Y * invW;

        // If completely off-screen, you can cull it (optional). We'll keep it simple:
        // Map NDC (-1..1) to screen (0..w/h). Flip Y for screen space.
        float sx = (ndcX * 0.5f + 0.5f) * w;
        float sy = (1f - (ndcY * 0.5f + 0.5f)) * h;

        screen = new Vector2(sx, sy);
        return true;
    }
}
