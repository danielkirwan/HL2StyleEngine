using System;
using System.Numerics;
using Veldrid;

namespace Engine.Render;

public sealed class Mesh : IDisposable
{
    public DeviceBuffer VertexBuffer { get; }
    public DeviceBuffer IndexBuffer { get; }
    public uint IndexCount { get; }

    public Mesh(GraphicsDevice gd, ReadOnlySpan<VertexPositionColor> vertices, ReadOnlySpan<ushort> indices)
    {
        var factory = gd.ResourceFactory;

        VertexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(vertices.Length * VertexPositionColor.SizeInBytes),
            BufferUsage.VertexBuffer));
        gd.UpdateBuffer(VertexBuffer, 0, vertices);

        IndexBuffer = factory.CreateBuffer(new BufferDescription(
            (uint)(indices.Length * sizeof(ushort)),
            BufferUsage.IndexBuffer));
        gd.UpdateBuffer(IndexBuffer, 0, indices);

        IndexCount = (uint)indices.Length;
    }

    public void Dispose()
    {
        IndexBuffer.Dispose();
        VertexBuffer.Dispose();
    }
}

public readonly struct VertexPositionColor
{
    public readonly Vector3 Position;
    public readonly RgbaFloat Color;

    public VertexPositionColor(Vector3 position, RgbaFloat color)
    {
        Position = position;
        Color = color;
    }

    public static uint SizeInBytes => (uint)(3 * sizeof(float) + 4 * sizeof(float));
}
