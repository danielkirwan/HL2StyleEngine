using System;
using Veldrid;

namespace Engine.Render;

public sealed class ImGuiLayer : IDisposable
{
    private readonly ImGuiRenderer _imgui;

    public ImGuiLayer(GraphicsDevice gd, OutputDescription output, int width, int height)
    {
        _imgui = new ImGuiRenderer(gd, output, width, height);
    }

    public void Update(float dt, InputSnapshot snapshot) => _imgui.Update(dt, snapshot);
    public void Render(GraphicsDevice gd, CommandList cl) => _imgui.Render(gd, cl);
    public void Dispose() => _imgui.Dispose();
}
