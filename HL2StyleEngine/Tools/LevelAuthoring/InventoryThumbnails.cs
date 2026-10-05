using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Engine.Render;

internal static class InventoryThumbnails
{
    // Offline orthographic thumbnails from the project's own GLBs; never runs in a game frame.
    public static void Run(string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        foreach (var (icon, model) in new[] { ("DamagedCable", "ElectricalWires03_01"),
            ("SpareWire", "ElectricalWires03_02"), ("RepairedCable", "ElectricalWires03_03") })
        {
            var loaded = GlbModelLoader.Load(Path.Combine(root, "Game/Content/Models/ViewModels", model + ".glb"));
            Matrix4x4 rotation = Matrix4x4.CreateRotationY(.65f) * Matrix4x4.CreateRotationX(-.6f) * Matrix4x4.CreateRotationZ(-.3f);
            var vertices = loaded.Parts.SelectMany(p => p.Positions).Select(p => Vector3.Transform(p, rotation)).ToArray();
            Vector3 min = vertices.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
            Vector3 max = vertices.Aggregate(new Vector3(float.MinValue), Vector3.Max);
            float scale = 218f / Math.Max(max.X - min.X, max.Y - min.Y);
            Vector3 center = (min + max) / 2;
            Vector3 Project(Vector3 p)
            {
                var v = (Vector3.Transform(p, rotation) - center) * scale;
                return new Vector3(v.X + 128, 128 - v.Y, v.Z);
            }
            using var bitmap = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
            float[] depth = Enumerable.Repeat(float.NegativeInfinity, 256 * 256).ToArray();
            foreach (var part in loaded.Parts)
            {
                using var stream = part.BaseColorPng == null ? null : new MemoryStream(part.BaseColorPng);
                using var texture = stream == null ? null : new Bitmap(stream);
                for (int t = 0; t + 2 < part.Indices.Length; t += 3)
                {
                    int ia = (int)part.Indices[t], ib = (int)part.Indices[t + 1], ic = (int)part.Indices[t + 2];
                    Vector3 a = Project(part.Positions[ia]), b = Project(part.Positions[ib]), c = Project(part.Positions[ic]);
                    float area = Edge(a, b, c.X, c.Y);
                    if (Math.Abs(area) < .001f) continue;
                    Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                    float shade = .55f + .45f * Math.Abs(Vector3.Dot(normal, Vector3.Normalize(new Vector3(-.3f, -.6f, 1))));
                    int x0 = Math.Clamp((int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, 255);
                    int x1 = Math.Clamp((int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, 255);
                    int y0 = Math.Clamp((int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, 255);
                    int y1 = Math.Clamp((int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, 255);
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        float u = Edge(b, c, x + .5f, y + .5f) / area;
                        float v = Edge(c, a, x + .5f, y + .5f) / area;
                        float w = 1 - u - v;
                        if (u < 0 || v < 0 || w < 0) continue;
                        float z = a.Z * u + b.Z * v + c.Z * w;
                        if (z <= depth[y * 256 + x]) continue;
                        Vector4 color = part.Color;
                        if (texture != null && part.TexCoords != null)
                        {
                            Vector2 uv = part.TexCoords[ia] * u + part.TexCoords[ib] * v + part.TexCoords[ic] * w;
                            Color texel = texture.GetPixel(Math.Clamp((int)((uv.X - MathF.Floor(uv.X)) * texture.Width), 0, texture.Width - 1),
                                Math.Clamp((int)((uv.Y - MathF.Floor(uv.Y)) * texture.Height), 0, texture.Height - 1));
                            color *= new Vector4(texel.R, texel.G, texel.B, texel.A) / 255f;
                        }
                        if (color.W < .1f) continue;
                        depth[y * 256 + x] = z;
                        bitmap.SetPixel(x, y, Color.FromArgb((int)(color.W * 255), (int)Math.Clamp(color.X * shade * 255, 0, 255),
                            (int)Math.Clamp(color.Y * shade * 255, 0, 255), (int)Math.Clamp(color.Z * shade * 255, 0, 255)));
                    }
                }
            }
            string output = Path.Combine(root, "Game/Content/UI/Icons", icon + ".png");
            bitmap.Save(output, ImageFormat.Png);
            Console.WriteLine(output);
        }
    }
    private static float Edge(Vector3 a, Vector3 b, float x, float y) => (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
}
