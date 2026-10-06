using System.Diagnostics;
using Engine.Editor.Level;
using Engine.Render;

internal static class TextureLoadingBenchmark
{
    public static void Run(string root, bool cooked)
    {
        var level = LevelIO.Load(Path.Combine(root, "Game/Content/Levels/sixRoomTest.json"));
        var paths = level.Entities.Select(e => e.MeshPath).Where(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var models = new List<LoadedModel>();
        var timer = Stopwatch.StartNew();
        foreach (string? relative in paths)
        {
            string path = Path.Combine(root, "Game", relative!);
            LoadedModel model = GlbModelLoader.Load(path);
            TextureCooker.Prepare(model, cooked ? TextureCooker.CacheDirectory(path) : null, cook: false);
            models.Add(model);
        }
        timer.Stop();
        var textures = models.SelectMany(m => m.Parts)
            .SelectMany(p => new[] { p.BaseColorTexture, p.MaterialTexture, p.NormalTexture })
            .OfType<PreparedTexture>().DistinctBy(t => t.Key).ToArray();
        Console.WriteLine($"{(cooked ? "COOKED" : "RGBA")}: {paths.Length} source GLBs + CPU texture preparation = {timer.Elapsed.TotalMilliseconds:F2} ms; {textures.Length} unique textures; {textures.Sum(t => t.Bytes) / 1048576.0:F2} MiB mip payload; {TextureCooker.CacheHits} cache hits; {TextureCooker.FallbackDecodes} image decodes.");
        Console.WriteLine("Warm filesystem, fresh process, serial worker, no GPU upload or first-playable-frame measurement.");
        GC.KeepAlive(models);
    }
}
