using System.Runtime.InteropServices;

namespace Engine.UI.Native;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RmlUiRenderData
{
    public readonly IntPtr Commands;
    public readonly int CommandCount;
    public readonly int ViewportWidth;
    public readonly int ViewportHeight;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RmlUiRenderCommand
{
    public readonly IntPtr Vertices;
    public readonly int VertexCount;
    public readonly IntPtr Indices;
    public readonly int IndexCount;
    public readonly ulong TextureId;
    public readonly int ScissorEnabled;
    public readonly int ScissorX;
    public readonly int ScissorY;
    public readonly int ScissorWidth;
    public readonly int ScissorHeight;
    public readonly float TranslateX;
    public readonly float TranslateY;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct RmlUiVertex
{
    public readonly float X;
    public readonly float Y;
    public readonly float U;
    public readonly float V;
    public readonly uint ColorRgba;
}
