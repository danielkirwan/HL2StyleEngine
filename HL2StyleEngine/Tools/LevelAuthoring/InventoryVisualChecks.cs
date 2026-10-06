using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Engine.Input.Devices;
using Engine.UI;

internal static class InventoryVisualChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Flags)!.GetValue(value)!;
    private static object? Call(object value, string name, params object?[] args)
        => value.GetType().GetMethod(name, Flags)!.Invoke(value, args);
    private static void Require(bool result, string message)
    {
        if (!result) throw new InvalidDataException(message);
    }

    public static void Run(string root)
    {
        foreach (var size in new[] { (1300, 775), (1920, 1080), (800, 600) })
        {
            using var backend = RmlUiBackend.Probe(Path.Combine(AppContext.BaseDirectory, "Content/UI"),
                Path.Combine(root, "Native/runtimes/win-x64/HS2RmlUiBridge.dll"));
            backend.SubmitState(new GameplayUiState
            {
                InventoryOpen = true, GridWidth = 8, GridHeight = 8, PrimaryGridHeight = 4,
                ViewportWidth = size.Item1, ViewportHeight = size.Item2, SelectedSlot = 0,
                InventoryItems = [new() { Id = "Scrap", DisplayName = "Scrap", SlotIndex = 0 }]
            });
            backend.Update(new RmlUiFrameContext(null!, new InputState(), size.Item1, size.Item2, 1f / 60));
            Require(backend.IsReady, backend.Status);
            object api = Field<object>(backend, "_nativeApi");
            IntPtr context = Field<IntPtr>(backend, "_context");
            Call(api, "Render", context);
            object?[] args = [context, null];
            Require((bool)Call(api, "TryGetRenderData", args)!, "No native inventory geometry.");
            try
            {
                var vertices = ReadUntexturedVertices(args[1]!);
                var backdrop = vertices.Where(v => v.Alpha == 158).ToArray();
                Require(backdrop.Length >= 4, "Inventory backdrop is missing or transparent; RCSS alpha must use 0..255.");
                Require(backdrop.Min(v => v.Position.X) <= 0 && backdrop.Min(v => v.Position.Y) <= 0 &&
                    backdrop.Max(v => v.Position.X) >= size.Item1 && backdrop.Max(v => v.Position.Y) >= size.Item2,
                    $"Inventory backdrop does not cover the viewport: " +
                    $"({backdrop.Min(v => v.Position.X)}, {backdrop.Min(v => v.Position.Y)}) to " +
                    $"({backdrop.Max(v => v.Position.X)}, {backdrop.Max(v => v.Position.Y)}) vs {size}.");
                var layout = new InventoryLayout(size.Item1, size.Item2, 8, 8, 4);
                for (int slot = 0; slot < 64; slot++)
                {
                    Vector2 min = layout.SlotOrigin(slot), max = min + new Vector2(layout.Cell);
                    bool Inside(Vector2 p) => p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
                    Require(vertices.Count(v => v.Alpha == 102 && Inside(v.Position)) >= 4,
                        $"Inventory slot {slot} has no visible background at {size}.");
                    Require(vertices.Count(v => v.Alpha == 138 && Inside(v.Position)) >= 4,
                        $"Inventory slot {slot} has no visible border at {size}.");
                }
                Require(vertices.Count(v => v.Alpha == 204) >= 4, "Item tile background is transparent.");
                Console.WriteLine($"PASS: native inventory backdrop, 64 cell backgrounds/borders and item tile at {size.Item1}x{size.Item2}.");
            }
            finally { Call(api, "ReleaseRenderData", context); }
        }
    }

    private static List<(Vector2 Position, byte Alpha)> ReadUntexturedVertices(object data)
    {
        Assembly assembly = typeof(GameplayUiLayer).Assembly;
        Type commandType = assembly.GetType("Engine.UI.Native.RmlUiRenderCommand")!;
        Type vertexType = assembly.GetType("Engine.UI.Native.RmlUiVertex")!;
        int commandSize = Marshal.SizeOf(commandType), vertexSize = Marshal.SizeOf(vertexType);
        var result = new List<(Vector2, byte)>();
        for (int i = 0; i < Field<int>(data, "CommandCount"); i++)
        {
            object command = Marshal.PtrToStructure(Field<IntPtr>(data, "Commands") + i * commandSize, commandType)!;
            if (Field<ulong>(command, "TextureId") != 0) continue;
            Vector2 translation = new(Field<float>(command, "TranslateX"), Field<float>(command, "TranslateY"));
            for (int j = 0; j < Field<int>(command, "VertexCount"); j++)
            {
                object vertex = Marshal.PtrToStructure(Field<IntPtr>(command, "Vertices") + j * vertexSize, vertexType)!;
                result.Add((new Vector2(Field<float>(vertex, "X"), Field<float>(vertex, "Y")) + translation,
                    (byte)(Field<uint>(vertex, "ColorRgba") >> 24)));
            }
        }
        return result;
    }
}
