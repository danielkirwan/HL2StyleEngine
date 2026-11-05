using System.Numerics;

namespace Game;

public sealed partial class HL2GameModule
{
    private readonly HashSet<string> _textureDestinationModels = new(StringComparer.OrdinalIgnoreCase);
    private string? _textureDestinationLevel;
    private long _textureDestinationRevision;
    public bool TextureDestinationPending => _textureDestinationLevel != null;
    public bool TextureDestinationReady { get; private set; }
    public string TextureDestinationError { get; private set; } = "";
    public int TextureDestinationModelCount => _textureDestinationModels.Count;

    // Texture/model readiness only; callers still own lighting, collision, spawn and transition policy.
    // Keep this request alive through reveal, then cancel it to release the temporary quality pins.
    public void PrepareTextureDestination(Vector3 position, float radius = 32)
    {
        if (!TextureStreamingTrialEnabled || _assetPackage == null)
            throw new InvalidOperationException("Destination preparation requires the package-backed texture trial.");
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z) ||
            !float.IsFinite(radius) || radius is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(position), "Use a finite position and radius between 1 and 128 metres.");
        CancelTextureDestination();
        _textureTrial?.Retry();
        _textureDestinationLevel = _editor.LevelPath;
        _textureDestinationRevision = _editor.Revision;
        foreach (var entity in _runtimeEntities)
        {
            if (!entity.Render.Enabled) continue;
            RenderBounds(entity, out var min, out var max);
            if (Vector3.DistanceSquared(position, Vector3.Clamp(position, min, max)) > radius * radius) continue;
            AddPath(entity.Render.ModelAssetPath, _textureDestinationModels);
        }
    }

    public void CancelTextureDestination()
    {
        _textureDestinationModels.Clear();
        _textureDestinationLevel = null;
        TextureDestinationReady = false;
        TextureDestinationError = "";
    }

    private void ValidateTextureDestination()
    {
        if (!TextureDestinationPending) return;
        if (_textureDestinationLevel == _editor.LevelPath && _textureDestinationRevision == _editor.Revision &&
            TextureStreamingTrialEnabled && !_editorEnabled) return;
        CancelTextureDestination();
        TextureDestinationError = "Destination preparation cancelled because the level or editing mode changed.";
    }

    private void UpdateTextureDestinationReadiness()
    {
        TextureDestinationReady = false;
        if (!TextureDestinationPending) return;
        var textures = _world.GetTextureResidency().GroupBy(t => t.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(t => t.FirstMip), StringComparer.Ordinal);
        foreach (string path in _textureDestinationModels)
        {
            if (!_weaponModelCache.TryGetValue(path, out var model)) return;
            if (model.Failed) { TextureDestinationError = "Destination model could not load: " + path; return; }
            if (model.Model == null || !_textureTrialModels.TryGetValue(path, out var info)) return;
            if (info.Uses.Any(t => !textures.TryGetValue(t.Key, out int mip) || mip != 0))
            {
                TextureDestinationError = _textureTrial?.Errors.FirstOrDefault() ?? "";
                return;
            }
        }
        TextureDestinationError = "";
        TextureDestinationReady = true;
    }
}
