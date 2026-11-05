using Veldrid;

namespace Engine.Render;

internal sealed partial class RenderModelPart
{
    internal IEnumerable<ModelTextureCache.SharedTexture> ActiveTextures()
    {
        if (_color != null) yield return _color;
        if (_material != null) yield return _material;
        if (_normal != null) yield return _normal;
        if (_emissive != null) yield return _emissive;
        foreach (var texture in _pbrTextures) yield return texture;
    }

    internal TextureChange? PrepareTextureChange(ModelTextureCache.SharedTexture replacement)
    {
        if (!ActiveTextures().Any(t => t.Info.Key == replacement.Info.Key && !ReferenceEquals(t, replacement))) return null;
        return new TextureChange(this, replacement);
    }

    internal sealed class TextureChange : IDisposable
    {
        private readonly RenderModelPart _owner;
        private TextureBindingState? _prepared;
        internal TextureChange(RenderModelPart owner, ModelTextureCache.SharedTexture replacement)
        {
            _owner = owner;
            var legacy = owner.TextureSet == null ? [] : new[] { owner._color!, owner._material!, owner._normal!, owner._emissive! };
            var pbr = owner._pbrTextures.ToArray();
            _prepared = new(owner._textures, Replace(legacy), Replace(pbr), null, null);
            foreach (var texture in _prepared.Legacy.Concat(_prepared.Pbr)) texture.References++;
            try
            {
                if (legacy.Length != 0) _prepared.LegacySet = Create(_prepared.Legacy);
                if (pbr.Length != 0) _prepared.PbrSet = Create(_prepared.Pbr);
            }
            catch { Dispose(); throw; }
            ModelTextureCache.SharedTexture[] Replace(ModelTextureCache.SharedTexture[] values)
                => values.Select(t => t.Info.Key == replacement.Info.Key ? replacement : t).ToArray();
            ResourceSet Create(ModelTextureCache.SharedTexture[] t) => owner._textureDevice.ResourceFactory.CreateResourceSet(
                new ResourceSetDescription(owner._textureLayout, t[0].View, owner._textureSampler, t[1].View,
                    owner._textureSampler, t[2].View, t[3].View));
        }

        internal void Commit(Renderer renderer)
        {
            var p = _prepared ?? throw new InvalidOperationException("Texture change already committed.");
            var old = new TextureBindingState(_owner._textures,
                _owner.TextureSet == null ? [] : [_owner._color!, _owner._material!, _owner._normal!, _owner._emissive!],
                _owner._pbrTextures.ToArray(), _owner.TextureSet, _owner.PbrTextureSet);
            _owner.TextureSet = p.LegacySet; _owner.PbrTextureSet = p.PbrSet;
            if (p.Legacy.Length != 0)
            { _owner._color = p.Legacy[0]; _owner._material = p.Legacy[1]; _owner._normal = p.Legacy[2]; _owner._emissive = p.Legacy[3]; }
            _owner._pbrTextures.Clear(); _owner._pbrTextures.AddRange(p.Pbr);
            _prepared = null; // The owner now holds these references and sets.
            renderer.RetireAfterFrame(old);
        }
        public void Dispose() { _prepared?.Dispose(); _prepared = null; }
    }

    private sealed class TextureBindingState(ModelTextureCache cache, ModelTextureCache.SharedTexture[] legacy,
        ModelTextureCache.SharedTexture[] pbr, ResourceSet? legacySet, ResourceSet? pbrSet) : IDisposable
    {
        public readonly ModelTextureCache.SharedTexture[] Legacy = legacy, Pbr = pbr;
        public ResourceSet? LegacySet = legacySet, PbrSet = pbrSet;
        public void Dispose()
        {
            LegacySet?.Dispose(); PbrSet?.Dispose();
            foreach (var texture in Legacy.Concat(Pbr)) cache.Release(texture);
        }
    }
}
