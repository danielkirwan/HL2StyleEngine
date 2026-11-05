using System.Diagnostics;

namespace Engine.Render;

public sealed partial class BasicWorldRenderer
{
    public TextureResidencyInfo[] GetTextureResidency() => _textures.ActiveTextures();

    // Call before drawing, not while a prepared draw sequence is being submitted.
    // Atomic per texture, including every legacy/PBR material using it; geometry stays intact.
    public bool TryReplaceTexture(PreparedTexture prepared, Renderer renderer, out int changedParts)
    {
        changedParts = 0;
        if (_uploadMilliseconds >= UploadBudgetMilliseconds) return false;
        long start = Stopwatch.GetTimestamp();
        try { changedParts = _textures.Replace(prepared, renderer); return true; }
        finally { _uploadMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }
}
