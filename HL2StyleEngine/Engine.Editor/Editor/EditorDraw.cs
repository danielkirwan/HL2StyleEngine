using System.Numerics;

namespace Engine.Editor.Editor;

public readonly struct EditorDrawBox
{
    public readonly Vector3 Position;
    public readonly Vector3 Size;
    public readonly Vector4 Color;
    public readonly Quaternion Rotation;
    public readonly bool IsSphere;

    public EditorDrawBox(Vector3 position, Vector3 size, Vector4 color, Quaternion rotation, bool isSphere = false)
    {
        Position = position;
        Size = size;
        Color = color;
        Rotation = rotation;
        IsSphere = isSphere;
    }

    public static EditorDrawBox AxisAligned(Vector3 position, Vector3 size, Vector4 color)
        => new(position, size, color, Quaternion.Identity);

    public static EditorDrawBox Sphere(Vector3 position, float diameter, Vector4 color)
        => new(position, new Vector3(diameter), color, Quaternion.Identity, isSphere: true);
}
