namespace Engine.Render;

public sealed record TextureResidencyInfo(string Key, TextureSemantic Semantic, bool Compressed,
    int Width, int Height, int FirstMip, int MipCount);

internal sealed partial class ModelTextureCache
{
    // Binding owners unregister on disposal. Access, preparation and commit are render-thread only.
    private readonly HashSet<RenderModelPart> _owners = new();
    internal void Track(RenderModelPart owner) => _owners.Add(owner);
    internal void Untrack(RenderModelPart owner) => _owners.Remove(owner);
    internal TextureResidencyInfo[] ActiveTextures() => _owners.SelectMany(o => o.ActiveTextures())
        .Distinct().Select(t => t.Info).Distinct().ToArray();

    internal int Replace(PreparedTexture source, Renderer renderer)
    {
        if (source.Width < 1 || source.Height < 1 || source.FirstMip is < 0 or > 14 || source.Mips.Length is < 1 or > 15 ||
            source.FirstMip + source.Mips.Length > 15)
            throw new InvalidDataException("Invalid replacement mip dimensions.");
        for (int mip = 0; mip < source.Mips.Length; mip++)
        {
            long w = Math.Max(1, source.Width >> mip), h = Math.Max(1, source.Height >> mip);
            long bytes = source.Compressed ? ((w + 3) / 4) * ((h + 3) / 4) * 16 : w * h * 4;
            if (source.Mips[mip] == null || source.Mips[mip].LongLength != bytes)
                throw new InvalidDataException("Invalid replacement mip payload.");
        }
        var old = _owners.SelectMany(o => o.ActiveTextures()).Distinct().Where(t => t.Info.Key == source.Key).ToArray();
        if (old.Length == 0) return 0;
        foreach (var t in old)
        {
            var i = t.Info;
            int common = Math.Max(i.FirstMip, source.FirstMip);
            if (i.Semantic != source.Semantic || i.Compressed != source.Compressed ||
                i.FirstMip + i.MipCount != source.FirstMip + source.Mips.Length ||
                Math.Max(1, i.Width >> (common - i.FirstMip)) != Math.Max(1, source.Width >> (common - source.FirstMip)) ||
                Math.Max(1, i.Height >> (common - i.FirstMip)) != Math.Max(1, source.Height >> (common - source.FirstMip)))
                throw new InvalidDataException("Replacement texture identity/dimensions differ: " + source.Key);
        }
        var replacement = Acquire(source);
        var changes = new List<RenderModelPart.TextureChange>();
        try
        {
            foreach (var owner in _owners)
                if (owner.PrepareTextureChange(replacement) is { } change) changes.Add(change);
            // All uploads/bindings must succeed before any visible owner changes.
            foreach (var change in changes) change.Commit(renderer);
            return changes.Count;
        }
        finally
        {
            foreach (var change in changes) change.Dispose();
            Release(replacement);
        }
    }
}
