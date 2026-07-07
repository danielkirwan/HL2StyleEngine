using Engine.Render;
using ImGuiNET;
using System.Numerics;

namespace HS2Editor;

internal sealed partial class HS2EditorModule
{
    private sealed class AssetPreviewEntry
    {
        public LoadedModel? Model;
        public Vector3 Min;
        public Vector3 Max;
        public string Error = "";
    }

    private readonly struct PreviewTriangle
    {
        public PreviewTriangle(Vector2 a, Vector2 b, Vector2 c, float depth, uint fillColor, uint lineColor)
        {
            A = a;
            B = b;
            C = c;
            Depth = depth;
            FillColor = fillColor;
            LineColor = lineColor;
        }

        public readonly Vector2 A;
        public readonly Vector2 B;
        public readonly Vector2 C;
        public readonly float Depth;
        public readonly uint FillColor;
        public readonly uint LineColor;
    }

    private void SelectAsset(string absolutePath)
    {
        _selectedAssetAbsolutePath = Path.GetFullPath(absolutePath);
        _selectedAssetProjectPath = MakeProjectRelative(_selectedAssetAbsolutePath);
    }

    private void DrawSelectedAssetPreviewPane()
    {
        ImGui.BeginChild("Asset Preview", new Vector2(0f, 0f), ImGuiChildFlags.Borders);
        Vector2 min = ImGui.GetCursorScreenPos();
        Vector2 size = ImGui.GetContentRegionAvail();
        size.X = MathF.Max(1f, size.X);
        size.Y = MathF.Max(1f, size.Y);

        ImGui.InvisibleButton("##assetPreviewCanvas", size);
        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 max = min + size;
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.025f, 0.028f, 0.032f, 1f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.25f, 0.7f)));

        if (string.IsNullOrWhiteSpace(_selectedAssetAbsolutePath))
        {
            DrawPreviewText(drawList, min, "Select a model to preview.");
            ImGui.EndChild();
            return;
        }

        if (!File.Exists(_selectedAssetAbsolutePath))
        {
            DrawPreviewText(drawList, min, "Selected model is missing.");
            ImGui.EndChild();
            return;
        }

        if (!TryGetAssetPreview(_selectedAssetAbsolutePath, out AssetPreviewEntry entry))
        {
            DrawPreviewText(drawList, min, entry.Error);
            ImGui.EndChild();
            return;
        }

        DrawPreviewModel(drawList, min, size, entry);
        string label = string.IsNullOrWhiteSpace(_selectedAssetProjectPath)
            ? Path.GetFileName(_selectedAssetAbsolutePath)
            : _selectedAssetProjectPath;
        drawList.AddText(min + new Vector2(8f, 8f), ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.55f, 1f)), label);
        ImGui.EndChild();
    }

    private static void DrawPreviewText(ImDrawListPtr drawList, Vector2 min, string text)
        => drawList.AddText(min + new Vector2(8f, 8f), ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.82f, 1f)), text);

    private bool TryGetAssetPreview(string absolutePath, out AssetPreviewEntry entry)
    {
        absolutePath = Path.GetFullPath(absolutePath);
        if (_assetPreviewCache.TryGetValue(absolutePath, out entry!))
            return entry.Model != null;

        entry = new AssetPreviewEntry();
        try
        {
            LoadedModel model = GlbModelLoader.Load(absolutePath);
            ComputeModelBounds(model, out Vector3 min, out Vector3 max);
            entry.Model = model;
            entry.Min = min;
            entry.Max = max;
        }
        catch (Exception ex)
        {
            entry.Error = $"Preview failed: {ex.Message}";
        }

        _assetPreviewCache[absolutePath] = entry;
        return entry.Model != null;
    }

    private static void ComputeModelBounds(LoadedModel model, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity);

        foreach (LoadedModelPart part in model.Parts)
        {
            foreach (Vector3 p in part.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        if (!float.IsFinite(min.X) || !float.IsFinite(max.X))
        {
            min = -Vector3.One;
            max = Vector3.One;
        }
    }

    private void DrawPreviewModel(ImDrawListPtr drawList, Vector2 min, Vector2 size, AssetPreviewEntry entry)
    {
        if (entry.Model == null)
            return;

        Vector3 center = (entry.Min + entry.Max) * 0.5f;
        Vector3 boundsSize = Vector3.Max(entry.Max - entry.Min, new Vector3(0.001f));
        float extent = MathF.Max(boundsSize.X, MathF.Max(boundsSize.Y, boundsSize.Z));
        float scale = 1.85f / extent;
        Matrix4x4 rotation =
            Matrix4x4.CreateRotationX(-0.35f) *
            Matrix4x4.CreateRotationY(_assetPreviewYaw);

        Vector2 screenCenter = min + size * 0.5f + new Vector2(0f, size.Y * 0.04f);
        float pixelScale = MathF.Min(size.X, size.Y) * 0.34f;
        float cameraDistance = 3.0f;
        float focal = 1.8f;

        List<PreviewTriangle> triangles = new(2048);
        const int maxTriangles = 4500;

        foreach (LoadedModelPart part in entry.Model.Parts)
        {
            Vector4 baseColor = part.Color;
            for (int i = 0; i + 2 < part.Indices.Length && triangles.Count < maxTriangles; i += 3)
            {
                Vector3 p0 = TransformPreviewPoint(part.Positions[part.Indices[i]], center, scale, rotation);
                Vector3 p1 = TransformPreviewPoint(part.Positions[part.Indices[i + 1]], center, scale, rotation);
                Vector3 p2 = TransformPreviewPoint(part.Positions[part.Indices[i + 2]], center, scale, rotation);

                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
                if (normal.LengthSquared() < 0.000001f)
                    continue;

                normal = Vector3.Normalize(normal);
                float light = Math.Clamp(Vector3.Dot(normal, Vector3.Normalize(new Vector3(-0.35f, 0.65f, -0.65f))) * 0.55f + 0.55f, 0.22f, 1f);

                Vector2 s0 = ProjectPreviewPoint(p0, screenCenter, pixelScale, cameraDistance, focal);
                Vector2 s1 = ProjectPreviewPoint(p1, screenCenter, pixelScale, cameraDistance, focal);
                Vector2 s2 = ProjectPreviewPoint(p2, screenCenter, pixelScale, cameraDistance, focal);

                if (!TriangleTouchesRect(s0, s1, s2, min, min + size))
                    continue;

                Vector4 fill = new(
                    Math.Clamp(baseColor.X * light, 0f, 1f),
                    Math.Clamp(baseColor.Y * light, 0f, 1f),
                    Math.Clamp(baseColor.Z * light, 0f, 1f),
                    0.92f);
                Vector4 line = new(1f, 0.9f, 0.22f, 0.34f);
                float depth = (p0.Z + p1.Z + p2.Z) / 3f;
                triangles.Add(new PreviewTriangle(
                    s0,
                    s1,
                    s2,
                    depth,
                    ImGui.GetColorU32(fill),
                    ImGui.GetColorU32(line)));
            }
        }

        triangles.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));
        drawList.PushClipRect(min, min + size, true);
        foreach (PreviewTriangle tri in triangles)
        {
            drawList.AddTriangleFilled(tri.A, tri.B, tri.C, tri.FillColor);
            drawList.AddTriangle(tri.A, tri.B, tri.C, tri.LineColor, 1f);
        }
        drawList.PopClipRect();
    }

    private static Vector3 TransformPreviewPoint(Vector3 point, Vector3 center, float scale, Matrix4x4 rotation)
        => Vector3.Transform((point - center) * scale, rotation);

    private static Vector2 ProjectPreviewPoint(Vector3 point, Vector2 screenCenter, float pixelScale, float cameraDistance, float focal)
    {
        float z = point.Z + cameraDistance;
        float perspective = focal / MathF.Max(0.25f, z);
        return screenCenter + new Vector2(point.X * perspective * pixelScale, -point.Y * perspective * pixelScale);
    }

    private static bool TriangleTouchesRect(Vector2 a, Vector2 b, Vector2 c, Vector2 min, Vector2 max)
    {
        float triMinX = MathF.Min(a.X, MathF.Min(b.X, c.X));
        float triMinY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
        float triMaxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
        float triMaxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
        return triMaxX >= min.X && triMaxY >= min.Y && triMinX <= max.X && triMinY <= max.Y;
    }
}