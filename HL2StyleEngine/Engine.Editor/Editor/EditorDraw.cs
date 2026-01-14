using System.Numerics;

namespace Engine.Editor.Editor;

public readonly struct EditorDrawBox
{
    public readonly Vector3 Position;
    public readonly Vector3 Size;
    public readonly Vector4 Color;
    public readonly Quaternion Rotation; 

    public EditorDrawBox(Vector3 position, Vector3 size, Vector4 color, Quaternion rotation)
    {
        Position = position;
        Size = size;
        Color = color;
        Rotation = rotation;
    }

    public static EditorDrawBox AxisAligned(Vector3 position, Vector3 size, Vector4 color)
        => new(position, size, color, Quaternion.Identity);
}
