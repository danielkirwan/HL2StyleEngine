using Engine.Runtime.Hosting;
using ImGuiNET;
using Veldrid;

internal static class ImGuiLayoutChecks
{
    internal static void Run()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "imgui.ini");
        byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
        string? profile = Environment.GetEnvironmentVariable("HS2_PROFILE_FRAMES");
        string? report = Environment.GetEnvironmentVariable("HS2_FRAME_REPORT");
        const string fixture = "[Window][AcceptanceLayoutSentinel]\nPos=123,87\nSize=640,400\nCollapsed=0\n\n";
        try
        {
            File.WriteAllText(path, fixture);
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", "2");
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", null);
            for (int i = 0; i < 2; i++)
            {
                using var host = new EngineHost(800, 600, "ImGui layout acceptance");
                if ((ImGui.GetIO().ConfigFlags & ImGuiConfigFlags.DockingEnable) == 0)
                    throw new InvalidDataException("Renderer construction lost DockingEnable.");
                string settings = ImGui.SaveIniSettingsToMemory();
                if (!settings.Contains("[Window][AcceptanceLayoutSentinel]") || !settings.Contains("Pos=123,87") || !settings.Contains("Size=640,400"))
                    throw new InvalidDataException("Renderer construction lost saved window placement.");
                host.Run(new LayoutModule());
            }
            Console.WriteLine("PASS: docking and saved placement survive rendered frames and two renderer lifetimes.");
        }
        finally
        {
            if (previous != null) File.WriteAllBytes(path, previous);
            else if (File.Exists(path)) File.Delete(path);
            Environment.SetEnvironmentVariable("HS2_PROFILE_FRAMES", profile);
            Environment.SetEnvironmentVariable("HS2_FRAME_REPORT", report);
        }
    }

    private sealed class LayoutModule : IGameModule
    {
        public void Initialize(EngineContext context) { }
        public void Update(float dt, InputSnapshot input) { }
        public void FixedUpdate(float dt) { }
        public void DrawImGui()
        {
            ImGui.Begin("AcceptanceLayoutSentinel");
            if (ImGui.GetWindowPos() != new System.Numerics.Vector2(123, 87) ||
                ImGui.GetWindowSize() != new System.Numerics.Vector2(640, 400))
                throw new InvalidDataException("Saved placement was not applied to the rendered window.");
            ImGui.TextUnformatted("Layout lifetime regression");
            ImGui.End();
        }
        public void Dispose() { }
    }
}
