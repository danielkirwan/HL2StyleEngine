using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Engine.Render;
using Engine.Runtime.Hosting;
using Engine.UI;

internal static class UiGeometryChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float X, Y, U, V; public uint Color; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Command
    {
        public IntPtr Vertices; public int VertexCount; public IntPtr Indices; public int IndexCount;
        public ulong TextureId; public int ScissorEnabled, ScissorX, ScissorY, ScissorWidth, ScissorHeight;
        public float TranslateX, TranslateY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Data { public IntPtr Commands; public int CommandCount, ViewportWidth, ViewportHeight; }

    internal static void Run(string? reference)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "qa", "ui-geometry-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(folder);
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks++; }
        var assembly = typeof(GameplayUiLayer).Assembly;
        var type = assembly.GetType("Engine.UI.Rendering.RmlUiOverlayRenderer")!;
        var dataType = assembly.GetType("Engine.UI.Native.RmlUiRenderData")!;
        Check(Marshal.SizeOf<Vertex>() == Marshal.SizeOf(assembly.GetType("Engine.UI.Native.RmlUiVertex")!), "Vertex ABI mismatch.");
        Check(Marshal.SizeOf<Command>() == Marshal.SizeOf(assembly.GetType("Engine.UI.Native.RmlUiRenderCommand")!), "Command ABI mismatch.");
        Check(Marshal.SizeOf<Data>() == Marshal.SizeOf(dataType), "Data ABI mismatch.");
        using var host = new EngineHost(640, 480, "UI geometry regression");
        var renderer = host.Context.Renderer;
        using var target = new PbrBaselineChecks.ImageTarget(renderer);
        using var ui = (IDisposable)Activator.CreateInstance(type)!;
        var render = type.GetMethod("Render")!;
        int Submitted() => (int)type.GetProperty("LastSubmittedCommands")!.GetValue(ui)!;
        Vertex[] vertices = [new() { X = 0, Y = 0, Color = 0xffffffff }, new() { X = 50, Y = 0, U = 1, Color = 0xffffffff },
            new() { X = 50, Y = 50, U = 1, V = 1, Color = 0xffffffff }, new() { X = 0, Y = 50, V = 1, Color = 0xffffffff }];
        int[] indices = [0, 1, 2, 0, 2, 3];
        var commands = new Command[512];
        var vertexHandle = GCHandle.Alloc(vertices, GCHandleType.Pinned);
        var indexHandle = GCHandle.Alloc(indices, GCHandleType.Pinned);
        var commandHandle = GCHandle.Alloc(commands, GCHandleType.Pinned);
        IntPtr dataPointer = Marshal.AllocHGlobal(Marshal.SizeOf<Data>());
        try
        {
            for (int i = 0; i < commands.Length; i++) commands[i] = new Command
            {
                Vertices = vertexHandle.AddrOfPinnedObject(), VertexCount = 4, Indices = indexHandle.AddrOfPinnedObject(), IndexCount = 6,
                TextureId = i % 3 == 0 ? 42ul : i % 3 == 1 ? 999ul : 0,
                TranslateX = i % 10 * 60, TranslateY = i / 10 % 8 * 60,
                ScissorEnabled = i % 2, ScissorX = 20, ScissorY = 20, ScissorWidth = 580, ScissorHeight = 440
            };
            var raw = new Data { Commands = commandHandle.AddrOfPinnedObject(), CommandCount = 64, ViewportWidth = 640, ViewportHeight = 480 };
            object BoxData() { Marshal.StructureToPtr(raw, dataPointer, false); return Marshal.PtrToStructure(dataPointer, dataType)!; }
            object?[] arguments = [renderer, BoxData(), true];
            void Draw() => render.Invoke(ui, arguments);
            uint[] Frame(bool read = false) => target.Frame(() => { }, read, Draw);
            type.GetMethod("RegisterTextureRgba")!.Invoke(ui, [42ul, renderer, 1, 1, new byte[] { 50, 160, 230, 255 }]);
            Frame();
            uint[] pixels = Frame(true);
            Check(Submitted() == 64, "Valid commands were lost.");
            Check((int)type.GetProperty("LastFallbackTextureCommands")!.GetValue(ui)! > 0, "Unknown texture fallback lost.");
            Check(pixels.Distinct().Count() > 2, "UI output is blank.");
            RenderPipelineChecks.SavePng(pixels, 640, 480, Path.Combine(folder, "geometry.png"));
            using (var output = File.Create(Path.Combine(folder, "pixels.bin")))
                output.Write(MemoryMarshal.AsBytes(pixels.AsSpan()));
            if (reference != null)
                Check(File.ReadAllBytes(Path.Combine(reference, "pixels.bin")).AsSpan().SequenceEqual(MemoryMarshal.AsBytes(pixels.AsSpan())), "UI pixels changed from the pre-fix reference.");
            for (int i = 0; i < 20; i++) Frame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            const int iterations = 180;
            for (int i = 0; i < iterations; i++) Frame();
            long allocation = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
            Console.WriteLine($"[UiGeometry] Warm 64-command overlay: {allocation:N0} bytes/frame.");
            if (reference != null)
            {
                using var prior = JsonDocument.Parse(File.ReadAllText(Path.Combine(reference, "summary.json")));
                Check(allocation < prior.RootElement.GetProperty("BytesPerFrame").GetInt64() / 10, "UI allocation did not fall by at least 90%.");
            }
            void Reject(Action corrupt, Action restore, string message)
            {
                corrupt(); arguments[1] = BoxData(); Frame(); Check(Submitted() == 0, message);
                restore(); arguments[1] = BoxData(); Frame(); Check(Submitted() == 64, "Rejected data contaminated the next valid frame.");
            }
            Reject(() => indices[0] = -1, () => indices[0] = 0, "Negative index accepted.");
            Reject(() => indices[0] = 4, () => indices[0] = 0, "Out-of-range index accepted.");
            Reject(() => vertices[0].X = float.NaN, () => vertices[0].X = 0, "Nonfinite position accepted.");
            Reject(() => vertices[0].U = float.PositiveInfinity, () => vertices[0].U = 0, "Nonfinite UV accepted.");
            Reject(() => commands[0].VertexCount = 65537, () => commands[0].VertexCount = 4, "Vertex limit bypassed.");
            Reject(() => commands[0].IndexCount = 131073, () => commands[0].IndexCount = 6, "Index limit bypassed.");
            Reject(() => raw.CommandCount = 513, () => raw.CommandCount = 64, "Command limit bypassed.");
            Reject(() => raw.Commands = IntPtr.Zero, () => raw.Commands = commandHandle.AddrOfPinnedObject(), "Missing commands reused prior geometry.");
            raw.CommandCount = 512; arguments[1] = BoxData(); Frame(); Check(Submitted() == 512, "Bounded command growth failed.");
            raw.CommandCount = 1; raw.ViewportWidth = 320; raw.ViewportHeight = 240;
            arguments[1] = BoxData(); Frame(); Check(Submitted() == 1, "Resize/smaller draw submitted old commands.");
            type.GetMethod("UnregisterTexture")!.Invoke(ui, [42ul]); Frame();
            Check((int)type.GetProperty("LastFallbackTextureCommands")!.GetValue(ui)! == 1, "Texture removal did not restore fallback.");
            raw.CommandCount = 0; arguments[1] = BoxData(); Frame(); Check(Submitted() == 0, "Empty frame replayed old commands.");
            if (reference != null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                int Capacity(object owner, string field)
                {
                    object list = type.GetField(field, flags)!.GetValue(owner)!;
                    return (int)list.GetType().GetProperty("Capacity")!.GetValue(list)!;
                }
                var largeVertices = GCHandle.Alloc(new Vertex[65536], GCHandleType.Pinned);
                var largeIndices = GCHandle.Alloc(new int[131072], GCHandleType.Pinned);
                Command saved = commands[0];
                try
                {
                    commands[0].Vertices = largeVertices.AddrOfPinnedObject(); commands[0].VertexCount = 40000;
                    commands[0].Indices = largeIndices.AddrOfPinnedObject(); commands[0].IndexCount = 100000;
                    raw.CommandCount = 1; arguments[1] = BoxData(); Frame();
                    Check(Submitted() == 1 && Capacity(ui, "_vertices") <= 65536 && Capacity(ui, "_indices") <= 131072, "Staging growth exceeded bounds.");
                    commands[0].VertexCount = 65536; commands[0].IndexCount = 131072; Frame();
                    Check(Submitted() == 1 && Capacity(ui, "_vertices") == 65536 && Capacity(ui, "_indices") == 131072, "Maximum geometry failed.");
                    raw.CommandCount = 2; arguments[1] = BoxData(); Frame();
                    Check(Submitted() == 0, "Aggregate vertex/index limit bypassed.");
                    commands[0].VertexCount = 4; Frame();
                    Check(Submitted() == 0, "Aggregate index limit bypassed.");
                }
                finally { commands[0] = saved; largeIndices.Free(); largeVertices.Free(); }
                raw.CommandCount = 1; arguments[1] = BoxData(); Frame();
                Check(Submitted() == 1, "Large staging contents leaked into a small draw.");
                var prepared = type.GetNestedType("PreparedCommand", BindingFlags.NonPublic)!;
                Check(prepared.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .All(f => f.FieldType == typeof(int) || f.FieldType == typeof(ulong)), "Persistent draw metadata retains native pointers.");
                var recreated = (IDisposable)Activator.CreateInstance(type)!;
                try
                {
                    type.GetMethod("Prepare")!.Invoke(recreated, [renderer]);
                    Check(Capacity(recreated, "_vertices") > 0, "Fresh renderer did not own staging storage.");
                }
                finally { recreated.Dispose(); }
                Check(Capacity(recreated, "_vertices") == 0 && Capacity(recreated, "_indices") == 0 && Capacity(recreated, "_preparedCommands") == 0,
                    "Disposed renderer retained staging storage.");
            }
            File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(new
            { Checks = checks, BytesPerFrame = allocation, Iterations = iterations, Reference = reference, Note = "Warm 64-command synthetic GPU overlay; includes test reflection/renderer overhead, not a frame-pacing benchmark." },
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {checks} UI geometry checks; {allocation:N0} bytes/frame. {folder}");
        }
        finally
        {
            renderer.GraphicsDevice.WaitForIdle();
            Marshal.FreeHGlobal(dataPointer); commandHandle.Free(); indexHandle.Free(); vertexHandle.Free();
        }
    }
}
