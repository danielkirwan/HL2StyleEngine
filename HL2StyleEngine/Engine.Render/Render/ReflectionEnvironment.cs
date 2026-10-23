using System.Numerics;
using Veldrid;

namespace Engine.Render;

public sealed class ReflectionEnvironment : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture _cube, _lut;
    private readonly TextureView _cubeView, _lutView;
    private readonly DeviceBuffer _metadata;
    private ReflectionEnvironmentData? _data;
    private int _next;
    internal ResourceSet Set { get; }
    public bool IsReady => _data == null;
    public long ResidentBytes { get; }
    public long PendingCpuBytes => _data?.PayloadBytes ?? 0;

    internal ReflectionEnvironment(GraphicsDevice device, ResourceLayout layout, Sampler sampler, ReflectionEnvironmentData data, bool enabled=true)
    {
        data.Validate(); _device=device; _data=data; ResidentBytes=data.PayloadBytes+16;
        var factory=device.ResourceFactory;
        _cube=factory.CreateTexture(TextureDescription.Texture2D((uint)data.Size,(uint)data.Size,(uint)data.MipCount,1,
            PixelFormat.R16_G16_B16_A16_Float,TextureUsage.Sampled|TextureUsage.Cubemap));
        _lut=factory.CreateTexture(TextureDescription.Texture2D((uint)data.LutSize,(uint)data.LutSize,1,1,PixelFormat.R16_G16_Float,TextureUsage.Sampled));
        _cubeView=factory.CreateTextureView(_cube); _lutView=factory.CreateTextureView(_lut);
        _metadata=factory.CreateBuffer(new BufferDescription(16,BufferUsage.UniformBuffer));
        device.UpdateBuffer(_metadata,0,new Vector4(data.MipCount-1,data.LutSize,enabled?1:0,0));
        Set=factory.CreateResourceSet(new ResourceSetDescription(layout,_cubeView,_lutView,sampler,_metadata));
    }

    public void UploadNext()
    {
        if (_data is not { } data) return;
        if (_next<data.Faces.Length)
        {
            uint mip=(uint)(_next/6), face=(uint)(_next%6), size=(uint)Math.Max(1,data.Size>>(int)mip);
            _device.UpdateTexture(_cube,data.Faces[_next],0,0,0,size,size,1,mip,face); _next++;
        }
        else
        {
            _device.UpdateTexture(_lut,data.Lut,0,0,0,(uint)data.LutSize,(uint)data.LutSize,1,0,0);
            _data=null;
        }
    }

    // Caller must retire through the renderer's frame fence when replacing a live environment.
    public void Dispose()
    { Set.Dispose(); _metadata.Dispose(); _cubeView.Dispose(); _lutView.Dispose(); _cube.Dispose(); _lut.Dispose(); _data=null; }
}
