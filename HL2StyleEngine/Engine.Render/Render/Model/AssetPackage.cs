using System.Security.Cryptography;
using System.Text.Json;
using System.Collections.Concurrent;
using Engine.Core.Diagnostics;

namespace Engine.Render;

public sealed record PackagedModel(string Path, string SourceHash, long Length, long LastWriteUtcTicks,
    string[] Textures);
public sealed record AssetPackageManifest(int Version, int ModelVersion, string Level, string LevelHash,
    PackagedModel[] Models);

// An explicit immutable playtest snapshot, never a substitute for editor source validation.
public sealed class AssetPackage
{
    public const int Version = 1;
    private readonly Dictionary<string, PackagedModel> _models;
    private sealed class TextureEntry
    {
        public WeakReference<PreparedTexture>? Data;
        public long Length, Ticks;
    }
    private readonly ConcurrentDictionary<string, TextureEntry> _textures = new();
    public string Root { get; }
    public string LevelPath { get; }
    public AssetPackageManifest Manifest { get; }
    private long _hits, _fallbacks;
    public long Hits => Interlocked.Read(ref _hits);
    public long Fallbacks => Interlocked.Read(ref _fallbacks);

    // Lighting may reuse only declared, unchanged immutable model identities. Shaders,
    // bake payloads and unknown/edited sources must still be hashed by the caller.
    public byte[]? TryGetModelIdentity(string path)
    {
        path = Path.GetFullPath(path);
        if (!_models.TryGetValue(path, out var entry)) return null;
        Resolve(Root, entry.Path);
        var info = new FileInfo(path);
        return info.Exists && info.Length == entry.Length && info.LastWriteTimeUtc.Ticks == entry.LastWriteUtcTicks
            ? Convert.FromHexString(entry.SourceHash) : null;
    }

    public AssetPackage(string manifestPath)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        Root = Path.GetDirectoryName(manifestPath)!;
        if (new FileInfo(manifestPath).Length > 8 * 1024 * 1024) throw new InvalidDataException("Package manifest is too large.");
        Manifest = JsonSerializer.Deserialize<AssetPackageManifest>(File.ReadAllBytes(manifestPath))
            ?? throw new InvalidDataException("Empty package manifest.");
        if (Manifest.Version != Version || Manifest.ModelVersion != CookedModelCache.FormatVersion ||
            Manifest.Models == null || Manifest.Models.Length > 4096) throw new InvalidDataException("Unsupported package manifest.");
        LevelPath = Resolve(Root, Manifest.Level);
        if (!HashValid(Manifest.LevelHash) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(LevelPath))).Equals(Manifest.LevelHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package level has changed. Rebuild the snapshot or use source mode.");
        _models = new(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Manifest.Models)
        {
            if (entry == null || !HashValid(entry.SourceHash) || entry.Length < 1 || entry.Textures == null || entry.Textures.Length > 16384)
                throw new InvalidDataException("Invalid model dependency metadata.");
            string path = Resolve(Root, entry.Path);
            if (!path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || !_models.TryAdd(path, entry))
                throw new InvalidDataException("Duplicate or unsupported model dependency.");
            foreach (string texture in entry.Textures)
                if (string.IsNullOrWhiteSpace(texture) || texture.Length > 128 || texture.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
                    throw new InvalidDataException("Invalid texture dependency key.");
        }
    }

    public LoadedModel Load(string path, LoadProfile? profile = null)
        => LoadMipTail(path, 0, profile);

    // Explicit opt-in only; normal game/editor/capture requests still use full mip chains.
    public LoadedModel LoadMipTail(string path, int firstMip, LoadProfile? profile = null)
    {
        if (firstMip < 0 || firstMip > 14) throw new ArgumentOutOfRangeException(nameof(firstMip));
        path = Path.GetFullPath(path);
        if (CookedModelCache.Enabled && _models.TryGetValue(path, out var entry))
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length == entry.Length && info.LastWriteTimeUtc.Ticks == entry.LastWriteUtcTicks)
            {
                using var scope = profile?.Measure("packaged-model-and-textures");
                Resolve(Root, Path.GetRelativePath(Root, CookedModelCache.PathForHash(path, Convert.FromHexString(entry.SourceHash))));
                var allowed = entry.Textures.ToHashSet(StringComparer.Ordinal);
                if (CookedModelCache.TryLoadPackaged(path, Convert.FromHexString(entry.SourceHash),
                    (key, semantic) => allowed.Contains(key) ? LoadTexture(path, key, semantic, firstMip) : null, out var model))
                { Interlocked.Increment(ref _hits); return model!; }
            }
        }
        Interlocked.Increment(ref _fallbacks);
        return CookedModelCache.Load(path, profile).MipTail(firstMip);
    }

    private PreparedTexture? LoadTexture(string modelPath, string key, TextureSemantic semantic, int firstMip)
    {
        string path = Resolve(Root, Path.GetRelativePath(Root, Path.Combine(TextureCooker.CacheDirectory(modelPath), key + ".hs2tex")));
        var entry = _textures.GetOrAdd(path + "|mip" + firstMip, _ => new());
        lock (entry)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (entry.Length == info.Length && entry.Ticks == info.LastWriteTimeUtc.Ticks &&
                entry.Data?.TryGetTarget(out var live) == true && live.Semantic == semantic) return live;
            if (!TextureCooker.TryReadMipTail(path, key, semantic, firstMip, out var texture, out _)) return null;
            entry.Length = info.Length; entry.Ticks = info.LastWriteTimeUtc.Ticks;
            entry.Data = new(texture!);
            return texture;
        }
    }

    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024 || Path.IsPathRooted(relative) ||
            relative.Contains(':') || relative.Split('/', '\\').Any(p => p is ".." or "." or ""))
            throw new InvalidDataException("Package paths must be relative and contained.");
        root = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package path escapes its root.");
        // Do not follow junctions/symlinks out of the snapshot.
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Package links are unsupported.");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return path;
    }

    private static bool HashValid(string? hash) => hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);
}
