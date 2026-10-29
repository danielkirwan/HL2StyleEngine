using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using Engine.Input.Devices;
using Engine.Runtime.Hosting;
using Engine.UI;

internal static class InventoryWarmupChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks++; }
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "inventory-warmup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        string scratch = Path.Combine(folder, "cache");
        Directory.CreateDirectory(Path.Combine(scratch, "Fonts"));
        string font = Path.Combine(scratch, "Fonts/AurelDeco-Regular.ttf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Content/UI/Fonts/AurelDeco-Regular.ttf"), font);

        Type type = typeof(GameplayUiLayer).Assembly.GetType("Engine.UI.Rml.RmlUiTextImageCache")!;
        using (var cache = (IDisposable)Activator.CreateInstance(type, scratch)!)
        {
            var resolve = type.GetMethod("TryGetTextImage")!;
            (bool Success, object Image) Get(string text)
            {
                object?[] args = [text, "title", 18, null];
                return ((bool)resolve.Invoke(cache, args)!, args[3]!);
            }
            string Source(object image) => (string)image.GetType().GetProperty("Source")!.GetValue(image)!;
            var first = Get("Inventory");
            Check(first.Success, "Text fixture did not render.");
            string path = Path.Combine(scratch, "Runtime", Source(first.Image));
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var metadata = (IDictionary)type.GetField("_metadata", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cache)!;
            for (int i = 0; i < 10; i++)
            {
                var hit = Get("Inventory");
                Check(hit.Success && hit.Image.Equals(first.Image) && metadata.Count == 1, "Metadata cache changed the text image.");
            }
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash, "Cached lookup rewrote text pixels.");
            File.WriteAllText(path, "invalid image");
            Check(!Get("Inventory").Success, "Changed/corrupt PNG returned stale metadata.");
            File.Delete(path);
            Check(Get("Inventory").Success && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash, "Deleted text image was not recreated identically.");
            File.SetLastWriteTimeUtc(font, File.GetLastWriteTimeUtc(font).AddSeconds(2));
            var changedFont = Get("Inventory");
            Check(changedFont.Success && Source(changedFont.Image) != Source(first.Image), "Font timestamp did not invalidate metadata identity.");
            for (int i = 0; i < 1030; i++) Check(Get("Label " + i).Success && metadata.Count <= 1024, "Text metadata cache exceeded its bound.");
        }

        string fallbackRoot = Path.Combine(folder, "fallback");
        var fallback = RmlUiBackend.Probe(fallbackRoot, "HS2_missing_test_bridge");
        using (var layer = new GameplayUiLayer(fallback, nativePresentationEnabled: true))
        {
            layer.Profile = new UiWorkProfile();
            layer.SubmitState(new GameplayUiState { InventoryOpen = true });
            layer.Update(new RmlUiFrameContext(null!, new InputState(), 800, 600, 1f / 60));
            Check(!Directory.Exists(Path.Combine(fallbackRoot, "Runtime")) && !layer.Profile.Snapshot().ContainsKey("document-build"), "Fallback generated unused native documents.");
            // Direct backend calls remain available to offline document tools.
            fallback.SubmitState(new GameplayUiState { InventoryOpen = true });
            Check(File.Exists(Path.Combine(fallbackRoot, "Runtime/gameplay_ui.rml")), "Explicit document generation stopped working.");
        }

        using var host = new EngineHost(800, 600, "Inventory warmup image checks");
        using var target = new PbrBaselineChecks.ImageTarget(host.Context.Renderer);
        uint[]? reference = null;
        string content = Path.Combine(AppContext.BaseDirectory, "Content/UI");
        foreach (bool warm in new[] { false, true })
        {
            using var backend = RmlUiBackend.Probe(content, Path.Combine(AppContext.BaseDirectory, "HS2RmlUiBridge.dll"));
            var input = new InputState();
            var context = new RmlUiFrameContext(host.Context.Renderer, input, target.Width, target.Height, 1f / 60);
            // Match gameplay's HUD-to-inventory transition on both sides. An initial
            // LoadDocument and a later SetDocumentBody are different native paths.
            backend.SubmitState(new GameplayUiState { HudVisible = true, LoadingOverlayVisible = true });
            backend.Update(context);
            if (warm)
            {
                string icon = Path.GetFullPath(Path.Combine(content, "Icons/DamagedCable.png"));
                backend.QueuePreloadIcon(icon, 256 * 256 * 4);
                backend.QueuePreloadIcon(icon, 256 * 256 * 4);
                backend.QueuePreloadIcon(Path.Combine(folder, "outside.png"), 4);
                backend.QueuePreloadIcon(Path.Combine(content, "..", "outside.png"), 4);
                backend.QueuePreloadIcon(Path.Combine(content, "Icons/oversize.png"), 17 * 1024 * 1024);
                Check(backend.PreloadedIconBytes == 256 * 256 * 4, "Native icon deduplication, root or budget guard failed.");
                backend.PrewarmOverlay(host.Context.Renderer);
                Check(backend.IsReady && !backend.HasSubmittedOverlayFrame && backend.LastSubmittedCommands == 0, "Warmup presented hidden UI.");
                backend.PrewarmOverlay(host.Context.Renderer);
                Check(!backend.HasSubmittedOverlayFrame, "Repeated warmup presented a frame.");
            }
            var state = new GameplayUiState
            {
                InventoryOpen = true, GridWidth = 8, GridHeight = 8, PrimaryGridHeight = 4,
                ViewportWidth = target.Width, ViewportHeight = target.Height, SelectedSlot = 0,
                InventoryItems = [new() { Id = "DamagedCable", DisplayName = "Damaged Cable", Description = "A broken conductor.", SlotIndex = 0,
                    SlotWidth = 1, SlotHeight = 1, IconPath = "../Icons/DamagedCable.png", Count = 1, MaxStack = 1 }]
            };
            backend.SubmitState(state);
            backend.Update(context);
            uint[] pixels = target.Frame(() => { }, overlay: () => backend.RenderOverlay(host.Context.Renderer));
            RenderPipelineChecks.SavePng(pixels, target.Width, target.Height, Path.Combine(folder, warm ? "prepared.png" : "on-demand.png"));
            // This deliberately monochrome UI has few colours; verify grid geometry
            // and visible pixels, not the colour-count gate used for textured worlds.
            Check(backend.LastSubmittedCommands >= 64 && pixels.Count(p => (p & 0x00ffffff) != 0) > 1000 && pixels.Distinct().Take(8).Count() == 8,
                $"Inventory capture failed: {backend.Status}; {backend.RenderStatus}; {pixels.Distinct().Count()} colours. {folder}");
            if (!warm) reference = pixels;
            else
            {
                int max = 0;
                for (int i = 0; i < pixels.Length; i++)
                    for (int shift = 0; shift < 24; shift += 8)
                        max = Math.Max(max, Math.Abs((int)(pixels[i] >> shift & 255) - (int)(reference![i] >> shift & 255)));
                Check(max <= 1, $"Prewarming changed inventory pixels by {max}/255.");
                Console.WriteLine($"[InventoryPixels] Prepared/on-demand maximum difference {max}/255.");
            }
        }
        Console.WriteLine($"PASS: {checks} inventory warmup assertions. {folder}");
    }
}
