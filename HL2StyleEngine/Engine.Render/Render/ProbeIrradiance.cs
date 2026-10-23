using System.Numerics;
using System.Runtime.InteropServices;

namespace Engine.Render;

// Six directional RGB samples packed into the existing 256-byte object upload slot.
[StructLayout(LayoutKind.Sequential)]
public struct ProbeIrradiance
{
    public Vector4 A, B, C, D, E;
    public readonly bool HasEnergy => A != Vector4.Zero || B != Vector4.Zero || C != Vector4.Zero || D != Vector4.Zero || E != Vector4.Zero;

    public static ProbeIrradiance Pack(ReadOnlySpan<Vector3> axes) => new()
    {
        A = new(axes[0], axes[1].X), B = new(axes[1].Y, axes[1].Z, axes[2].X, axes[2].Y),
        C = new(axes[2].Z, axes[3].X, axes[3].Y, axes[3].Z),
        D = new(axes[4], axes[5].X), E = new(axes[5].Y, axes[5].Z, 0, 0)
    };

    public readonly Vector3 Evaluate(Vector3 normal)
    {
        Vector3 x = normal.X >= 0 ? new(A.X,A.Y,A.Z) : new(A.W,B.X,B.Y);
        Vector3 y = normal.Y >= 0 ? new(B.Z,B.W,C.X) : new(C.Y,C.Z,C.W);
        Vector3 z = normal.Z >= 0 ? new(D.X,D.Y,D.Z) : new(D.W,E.X,E.Y);
        return x*(normal.X*normal.X) + y*(normal.Y*normal.Y) + z*(normal.Z*normal.Z);
    }
}
