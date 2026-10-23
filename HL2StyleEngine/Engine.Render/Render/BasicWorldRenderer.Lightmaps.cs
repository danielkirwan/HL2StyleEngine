using Veldrid;

namespace Engine.Render;

public sealed partial class BasicWorldRenderer
{
    public bool IndirectOnly { get; set; }
    private ResourceLayout? _lightmapLayout;
    private Sampler? _lightmapSampler;
    private LightmapAtlas? _emptyLightmap;
    private ResourceSet? _lightmapFallbackMaterial;
    private readonly List<ModelTextureCache.SharedTexture> _lightmapDefaults = new();

    private ResourceLayout CreateLightmapResources()
    {
        _lightmapLayout = _factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("LightmapTex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("LightmapSampler", ResourceKind.Sampler, ShaderStages.Fragment)));
        _lightmapSampler = _factory.CreateSampler(new SamplerDescription(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp,
            SamplerAddressMode.Clamp, SamplerFilter.MinLinear_MagLinear_MipPoint, null, 1, 0, 0, 0, SamplerBorderColor.TransparentBlack));
        _emptyLightmap = CreateLightmapAtlas(1, new byte[8]);
        return _lightmapLayout;
    }

    public LightmapAtlas CreateLightmapAtlas(int size, byte[] halfRgba)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // Only bake-enabled levels need fallback materials for untextured receivers.
            if (_lightmapFallbackMaterial == null && size > 1)
            {
                ModelTextureCache.SharedTexture Get(string key, TextureSemantic semantic, byte[] rgba)
                {
                    var texture = _textures.Acquire(new(key, 1, 1, semantic, false, [rgba]));
                    _lightmapDefaults.Add(texture); return texture;
                }
                var white = Get("default-white", TextureSemantic.Color, [255,255,255,255]);
                var material = Get("default-material", TextureSemantic.Material, [0,255,0,255]);
                var normal = Get("default-normal", TextureSemantic.Normal, [128,128,255,255]);
                _lightmapFallbackMaterial = _factory.CreateResourceSet(new ResourceSetDescription(_textureLayout,
                    white.View, _modelSampler, material.View, _modelSampler, normal.View, white.View));
            }
            return new LightmapAtlas(_gd, _lightmapLayout!, _lightmapSampler!, size, halfRgba);
        }
        finally { _uploadMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    public void UploadLightmapPart(LightmapBinding binding)
    {
        if (_uploadMilliseconds >= UploadBudgetMilliseconds || binding.IsReady) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { binding.UploadNext(_gd); }
        finally { _uploadMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    private void DisposeLightmaps()
    {
        _lightmapFallbackMaterial?.Dispose();
        foreach (var texture in _lightmapDefaults) _textures.Release(texture);
        _emptyLightmap?.Dispose(); _lightmapSampler?.Dispose(); _lightmapLayout?.Dispose();
    }
}
