using System.Numerics;
using Engine.Render;

namespace Game;

public sealed partial class HL2GameModule
{
    // Explicit package-only experiment. No level/save setting enables this in normal play.
    public bool TextureStreamingTrialEnabled { get; set; }
    public bool TextureStreamingFullResolution { get; set; }
    public int TextureStreamingBudgetMiB { get; set; } = 48;
    private TextureMipResidency? _textureTrial;
    private sealed record TextureUse(string Key, int Size, float UvSpan);
    private sealed record TextureModelInfo(WeakReference<LoadedModel> Source, TextureUse[] Uses);
    private readonly Dictionary<string, TextureModelInfo> _textureTrialModels = new(StringComparer.OrdinalIgnoreCase);
    public long TextureTrialResidentBytes => _world.TextureResidentBytes;
    public long TextureTrialEstimatedBytes => _textureTrial?.EstimatedBytes ?? 0;
    public int TextureTrialReplacements => _textureTrial?.Replacements ?? 0;
    public bool TextureTrialOverBudget => _textureTrial?.OverBudget == true;
    public IEnumerable<string> TextureTrialErrors => _textureTrial?.Errors ?? [];
    public bool TextureTrialBusy => _textureTrial?.Busy == true;
    public bool TextureTrialFullDetail => _world.GetTextureResidency().All(t => t.FirstMip == 0) && !TextureTrialBusy;

    private bool ReadyForTextureTrialEditor()
    {
        if (_textureTrial == null || _editorEnabled) return true;
        TextureStreamingFullResolution = true;
        if (TextureTrialFullDetail) return true;
        ShowGameMessage("Restoring full-detail textures. Press F2 again when ready.", 3);
        return false;
    }

    private void PumpTextureTrial(Renderer renderer)
    {
        if (_assetPackage == null || (!TextureStreamingTrialEnabled && _textureTrial == null)) return;
        _textureTrial ??= new();
        using (renderer.CpuProfile.Measure(RenderWork.TexturePlan)) PlanTextureTrial(renderer);
        using (renderer.CpuProfile.Measure(RenderWork.TexturePump))
        {
            _textureTrial.Pump(_world, renderer);
            UpdateTextureDestinationReadiness();
        }
    }

    private void PlanTextureTrial(Renderer renderer)
    {
        var trial = _textureTrial!;
        foreach (var (path, entry) in _weaponModelCache)
        {
            if (entry.LoadedModel is not { } source || entry.Model == null) continue;
            if (_textureTrialModels.TryGetValue(path, out var previous) && previous.Source.TryGetTarget(out var old) && ReferenceEquals(old, source)) continue;
            var uses = new List<TextureUse>();
            foreach (var part in source.Parts)
            {
                foreach (var (texture, uv) in new[] { (part.BaseColorTexture, part.TexCoords), (part.MaterialTexture, part.TexCoords),
                    (part.NormalTexture, part.TexCoords), (part.EmissiveTexture, part.EmissiveTexCoords ?? part.TexCoords) })
                {
                    if (texture == null) continue;
                    string key = texture.Key; var semantic = texture.Semantic;
                    var package = _assetPackage;
                    trial.Register(TextureMipSource.From(texture, mip => package!.LoadTextureMipTail(path, key, semantic, mip)));
                    float span = 0;
                    if (uv is { Length: > 0 })
                    {
                        Vector2 extent = uv.Aggregate(Vector2.Max) - uv.Aggregate(Vector2.Min);
                        span = MathF.Min(extent.X, extent.Y);
                    }
                    uses.Add(new(key, Math.Max(texture.Width, texture.Height), span));
                }
            }
            _textureTrialModels[path] = new(new(source), uses.ToArray());
        }
        bool full = !TextureStreamingTrialEnabled || TextureStreamingFullResolution || _editorEnabled;
        var demands = new List<TextureMipDemand>();
        var classified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in _runtimeEntities)
        {
            if (!entity.Render.Enabled || string.IsNullOrWhiteSpace(entity.Render.ModelAssetPath)) continue;
            string path = ResolveModelAssetPath(entity.Render.ModelAssetPath);
            if (!_textureTrialModels.TryGetValue(path, out var model)) continue;
            classified.Add(path);
            RenderBounds(entity, out var min, out var max);
            float radius = (max - min).Length() * .5f, distance = Vector3.Distance(_camera.Position, (min + max) * .5f);
            bool pin = full || entity.IsHeld || distance - radius < 8 || _residentPins.Contains(path) || _textureDestinationModels.Contains(path);
            bool prefetch = RoomVisibilityEnabled && _roomVisibility is { Active: true } && _roomVisibility.IsPrefetched(min, max);
            bool visible = RoomCanSee(min, max) && _world.IntersectsView(min, max, Matrix4x4.Identity);
            foreach (var use in model.Uses)
            {
                int mip = TextureMipResidency.ProjectedMip(use.Size, radius, distance, use.UvSpan, MathF.PI / 3,
                    (int)renderer.WorldFramebuffer.Height, trial.RequestedMip(use.Key));
                // Connected rooms must be sharp before their doorway is crossed, not upgraded after reveal.
                if (prefetch) mip = 0;
                // Protect visible/prefetched material demand even if the requested budget is impossible.
                demands.Add(new(use.Key, pin ? 0 : mip, (visible ? 20 : prefetch ? 10 : 1) / Math.Max(1, distance), pin,
                    visible || prefetch ? mip : 3));
            }
        }
        // Viewmodels, live fragments and unclassified owners never lose texture detail.
        foreach (var (path, info) in _textureTrialModels)
            if (full || !classified.Contains(path) || _residentPins.Contains(path) || _textureDestinationModels.Contains(path))
                demands.AddRange(info.Uses.Select(t => new TextureMipDemand(t.Key, 0, 100, true)));
        trial.Plan(demands, full ? long.MaxValue : Math.Clamp(TextureStreamingBudgetMiB, 8, 512) * 1048576L);
    }
}
