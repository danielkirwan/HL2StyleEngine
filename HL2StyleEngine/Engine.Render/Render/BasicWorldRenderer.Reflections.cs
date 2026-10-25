using System.Diagnostics;
using Veldrid;

namespace Engine.Render;

public sealed partial class BasicWorldRenderer
{
    private ResourceLayout _reflectionLayout = null!;
    private Sampler _reflectionSampler = null!;
    private ReflectionEnvironment _blackReflection = null!;
    private ReflectionEnvironment? _studioReflection;
    private Task<ReflectionEnvironmentData>? _reflectionLoad;
    private bool _reflectionRequested;
    private bool _reflectionFailed;
    public ReflectionPreset EnvironmentPreset { get; set; }
    public bool ReflectionPreviewEnabled { get; set; } = true;
    // Explicit resources are caller-owned; the built-in test preset is owned by this renderer.
    public ReflectionEnvironment? Reflection { get; set; }
    public ReflectionRooms? RoomReflections { get; set; }
    public string ReflectionStatus { get; private set; } = "Off";
    public bool ReflectionReady => RoomReflections != null || ActiveReflection != null;
    public long ReflectionResidentBytes => _blackReflection.ResidentBytes+(_studioReflection?.ResidentBytes??0)+(Reflection?.ResidentBytes??0);
    public long ReflectionPendingCpuBytes => _studioReflection?.PendingCpuBytes ?? 0;
    private ReflectionEnvironment? ActiveReflection => Reflection is {IsReady:true} ? Reflection :
        EnvironmentPreset==ReflectionPreset.SyntheticStudio && _studioReflection is {IsReady:true} ? _studioReflection : null;

    private ResourceLayout CreateReflectionResources()
    {
        _reflectionLayout=_factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("EnvironmentTex",ResourceKind.TextureReadOnly,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentBrdf",ResourceKind.TextureReadOnly,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentSampler",ResourceKind.Sampler,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("ReflectionSettings",ResourceKind.UniformBuffer,ShaderStages.Fragment),
            new ResourceLayoutElementDescription("EnvironmentTexB",ResourceKind.TextureReadOnly,ShaderStages.Fragment)));
        _reflectionSampler=_factory.CreateSampler(new SamplerDescription(SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,SamplerAddressMode.Clamp,
            SamplerFilter.MinLinear_MagLinear_MipLinear,null,1,0,uint.MaxValue,0,SamplerBorderColor.TransparentBlack));
        _blackReflection=new(_gd,_reflectionLayout,_reflectionSampler,new(1,1,1,Enumerable.Range(0,6).Select(_=>new byte[8]).ToArray(),new byte[4]),enabled:false);
        while (!_blackReflection.IsReady) _blackReflection.UploadNext();
        return _reflectionLayout;
    }

    public ReflectionEnvironment CreateReflectionEnvironment(ReflectionEnvironmentData data) => new(_gd,_reflectionLayout,_reflectionSampler,data);

    public ReflectionRooms CreateReflectionRooms(ReflectionRoom[] rooms, ReflectionDoorway[] doors, ReflectionEnvironment?[] environments)
        => new(_gd,_reflectionLayout,_reflectionSampler,_blackReflection,rooms,doors,environments);

    public bool UploadReflection(ReflectionEnvironmentData data, ref ReflectionEnvironment? resource)
    {
        if (_uploadMilliseconds >= UploadBudgetMilliseconds) return false;
        long start = Stopwatch.GetTimestamp();
        try
        {
            resource ??= CreateReflectionEnvironment(data);
            while (!resource.IsReady && _uploadMilliseconds + Stopwatch.GetElapsedTime(start).TotalMilliseconds < UploadBudgetMilliseconds)
                resource.UploadNext();
            return resource.IsReady;
        }
        finally { _uploadMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    private void UpdateReflectionResources()
    {
        if (LightingModel!=SurfaceLightingModel.Pbr || EnvironmentPreset!=ReflectionPreset.SyntheticStudio) return;
        if (!_reflectionRequested)
        {
            _reflectionRequested=true; ReflectionStatus="Loading synthetic environment";
            _reflectionLoad=Task.Run(() =>
            {
                using var stream=typeof(BasicWorldRenderer).Assembly.GetManifestResourceStream("Engine.Render.Assets.SyntheticStudio.hs2env")
                    ?? throw new FileNotFoundException("Cooked synthetic reflection environment is missing.");
                return ReflectionEnvironmentData.Read(stream);
            });
        }
        if (_reflectionLoad is {IsCompleted:true} && _uploadMilliseconds<UploadBudgetMilliseconds)
        {
            long start=Stopwatch.GetTimestamp();
            try { _studioReflection=CreateReflectionEnvironment(_reflectionLoad.GetAwaiter().GetResult()); ReflectionStatus="Uploading synthetic environment"; }
            catch (Exception ex) { ReflectionStatus="Reflections unavailable: "+ex.Message; }
            finally { _reflectionLoad=null; _uploadMilliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
        }
        while (!_reflectionFailed && _studioReflection is {IsReady:false} && _uploadMilliseconds<UploadBudgetMilliseconds)
        {
            long start=Stopwatch.GetTimestamp();
            try { _studioReflection.UploadNext(); }
            catch (Exception ex)
            {
                // An incomplete resource was never bound. Retain it until shutdown, not mid-frame disposal.
                _reflectionFailed=true; ReflectionStatus="Reflection upload failed: "+ex.Message;
            }
            finally { _uploadMilliseconds+=Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
        }
        if (_studioReflection is {IsReady:true}) ReflectionStatus="Synthetic environment ready";
    }

    private void DisposeReflections()
    {
        if (_reflectionLoad != null) _ = _reflectionLoad.ContinueWith(t=>{ _=t.Exception; },TaskScheduler.Default);
        _studioReflection?.Dispose(); _blackReflection.Dispose(); _reflectionSampler.Dispose(); _reflectionLayout.Dispose();
    }
}
