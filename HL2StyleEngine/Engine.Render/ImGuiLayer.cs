using System;
using System.IO;
using Veldrid;
using ImGuiNET;

namespace Engine.Render;

public sealed class ImGuiLayer : IDisposable
{
    private readonly ImGuiRenderer _imgui;

    private readonly string _iniPath;
    private double _saveTimer;

    public ImGuiLayer(GraphicsDevice gd, OutputDescription output, int width, int height)
    {
        _iniPath = Path.Combine(AppContext.BaseDirectory, "imgui.ini");

        if (ImGui.GetCurrentContext() == IntPtr.Zero)
            ImGui.CreateContext();

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        if (File.Exists(_iniPath))
        {
            try { ImGui.LoadIniSettingsFromDisk(_iniPath); }
            catch { }
        }
        _imgui = new ImGuiRenderer(gd, output, width, height);
    }

    public void WindowResized(int width, int height) => _imgui.WindowResized(width, height);

    public void Update(float dt, InputSnapshot snapshot)
    {
        _imgui.Update(dt, snapshot);

        _saveTimer += dt;
        if (_saveTimer >= 2.0)
        {
            _saveTimer = 0.0;
            TrySaveIni();
        }
    }

    public void Render(GraphicsDevice gd, CommandList cl) => _imgui.Render(gd, cl);

    public void Dispose()
    {
        TrySaveIni();
        _imgui.Dispose();
    }

    private void TrySaveIni()
    {
        try { ImGui.SaveIniSettingsToDisk(_iniPath); }
        catch {  }
    }
}
