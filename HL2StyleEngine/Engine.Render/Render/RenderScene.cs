using System.Numerics;
using System.Runtime.InteropServices;
using Veldrid;

namespace Engine.Render;

// Frame-local immutable values; resource handles borrow their owners' fenced lifetime.
// Capture scenes own separate records/resources, never the gameplay visible subset.
public readonly record struct RenderInstance(string Id, RenderModel Model, Matrix4x4 Transform,
    Vector4 Tint, Vector3 Center, float Radius, IReadOnlySet<string>? HiddenParts = null,
    ModelEmission? Emission = null, LightmapBinding? Lightmap = null, ProbeIrradiance Probe = default,
    bool Visible = true, bool CastShadow = true, bool AllowInstancing = false)
{
    public ShadowCaster Shadow => new(Model, Transform, Center, Radius, HiddenParts);
}

public sealed class RenderScene
{
    private readonly List<RenderInstance> _instances = new();
    private bool _sealed;
    public int Count => _instances.Count;
    public ReadOnlySpan<RenderInstance> Instances => CollectionsMarshal.AsSpan(_instances);
    public void Begin() { _instances.Clear(); _sealed = false; }
    public void Add(in RenderInstance instance)
    {
        if (_sealed) throw new InvalidOperationException("The render scene is already sealed for this frame.");
        _instances.Add(instance);
    }
    public void Seal() => _sealed = true;
    public void Draw(BasicWorldRenderer world, CommandList commands)
    {
        if (!_sealed) throw new InvalidOperationException("Seal the render scene before submission.");
        using var group = world.GroupModelDraws(commands);
        foreach (ref readonly var instance in Instances)
            if (instance.Visible) world.DrawInstance(commands, instance);
    }
}
