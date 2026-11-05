using System;
using System.IO;
using Veldrid;
using ImGuiNET;

namespace Engine.Render;

public sealed class ImGuiLayer : IDisposable
{
    private readonly ImGuiRenderer _imgui;
    private readonly GraphicsDevice _device;
    private readonly IntPtr _context;
    private readonly ImGuiImageCache _images = new();

    private readonly string _iniPath;
    private double _saveTimer;

    public ImGuiLayer(GraphicsDevice gd, OutputDescription output, int width, int height)
    {
        _device = gd;
        _iniPath = Path.Combine(AppContext.BaseDirectory, "imgui.ini");

        // This backend starts a frame in its constructor. Replace its bootstrap context
        // so docking and the saved layout are configured before our first NewFrame.
        _imgui = new ImGuiRenderer(gd, output, width, height);
        var backendFlags = ImGui.GetIO().BackendFlags;
        IntPtr bootstrap = ImGui.GetCurrentContext();
        ImGui.EndFrame();
        ImGui.DestroyContext(bootstrap);
        _context = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.BackendFlags = backendFlags;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        unsafe { io.NativePtr->IniFilename = null; }
        io.Fonts.AddFontDefault();

        if (File.Exists(_iniPath))
        {
            try { ImGui.LoadIniSettingsFromDisk(_iniPath); }
            catch { }
        }
        _imgui.RecreateFontDeviceTexture(gd);
        io.DisplaySize = new System.Numerics.Vector2(width, height);
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
        io.DeltaTime = 1f / 60f;
        ImGui.NewFrame();
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
    public UiImage GetImage(string path) => _images.Get(path, _device, _imgui);
    public long ImageResidentBytes => _images.ResidentBytes;
    public bool TryPreloadImage(string path, long budget) => _images.TryPreload(path, _device, _imgui, budget);

    public void Dispose()
    {
        TrySaveIni();
        _imgui.Dispose();
        _images.Dispose();
        ImGui.DestroyContext(_context);
    }

    private void TrySaveIni()
    {
        try { ImGui.SaveIniSettingsToDisk(_iniPath); }
        catch {  }
    }

}
