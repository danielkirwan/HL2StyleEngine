using System.Diagnostics;
using System.Security.Cryptography;
using Engine.Core.Diagnostics;

namespace Engine.Core.Assets;

// Per-session policy. Null means strict content hashing, including offline/editor baking.
public sealed class AssetFingerprintReader(LoadProfile? profile = null, Func<string, byte[]?>? immutableIdentity = null)
{
    private long _sourceBytes, _sourceReads, _identityHits;
    public LoadProfile? Profile { get; } = profile;
    public long SourceBytes => Interlocked.Read(ref _sourceBytes);
    public long SourceReads => Interlocked.Read(ref _sourceReads);
    public long IdentityHits => Interlocked.Read(ref _identityHits);

    public static byte[] ReadHash(string path, string stage, AssetFingerprintReader? reader = null,
        CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        long start = Stopwatch.GetTimestamp();
        byte[]? identity = reader == null ? null : reader.Resolve(path);
        if (identity is { Length: 32 })
        {
            cancel.ThrowIfCancellationRequested();
            Interlocked.Increment(ref reader!._identityHits);
            reader.Profile?.Record(stage + "-identity", Stopwatch.GetTimestamp() - start);
            return identity;
        }
        using var stream = File.OpenRead(path);
        long length = stream.Length;
        byte[] hash = SHA256.HashData(stream);
        if (reader != null)
        {
            Interlocked.Add(ref reader._sourceBytes, length);
            Interlocked.Increment(ref reader._sourceReads);
            reader.Profile?.Record(stage + "-read-hash", Stopwatch.GetTimestamp() - start, length);
        }
        cancel.ThrowIfCancellationRequested();
        return hash;
    }

    private byte[]? Resolve(string path) => immutableIdentity?.Invoke(path);
}
