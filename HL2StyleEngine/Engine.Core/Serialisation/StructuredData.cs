using System.Security.Cryptography;
using System.Text.Json;
using REDox.Serialization.SystemTextJson;

namespace Engine.Core.Serialization;

// JSON is the editable source of truth. DOX files are disposable, content-validated caches.
public static class StructuredData
{
    private static readonly JsonSerializerOptions LegacyOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private static readonly SystemTextJsonSerializerSettings Settings = new(LegacyOptions);
    private static readonly REDox.Json.JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true, EnableValueValidation = true, MaxDepth = 64
    };
    private static readonly REDox.Json.JsonWriteOptions WriteOptions = new()
    {
        WriteIndented = true, IndentSize = 2
    };
    private static ReadOnlySpan<byte> CacheMagic => "HS2DOX02"u8;
    private const int HeaderSize = 72;
    private const int MaxCacheBytes = 256 * 1024 * 1024;

    public static T ReadJson<T>(byte[] bytes)
    {
        // The old text reader accepted a UTF-8 BOM; REDox expects the JSON token first.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            bytes = bytes[3..];
        try
        {
            return (T?)REDox.Json.JsonSerializer.Deserialize(bytes, typeof(T), Settings, ReadOptions)
                ?? throw new InvalidDataException("The document contains null instead of an object.");
        }
        catch (REDox.DocumentParseException)
        {
            // Legacy authoring files permit comments. Only this compatibility path uses STJ.
            return JsonSerializer.Deserialize<T>(bytes, LegacyOptions)
                ?? throw new InvalidDataException("The document contains null instead of an object.");
        }
    }

    public static byte[] WriteJson<T>(T value)
        => REDox.Json.JsonSerializer.SerializeToUtf8Bytes(value!, typeof(T), Settings, WriteOptions);

    public static T Load<T>(string path) => ReadJson<T>(File.ReadAllBytes(path));

    public static void Save<T>(string path, T value)
        => WriteAtomic(path, WriteJson(value), keepBackup: true);

    public static string CachePath(string path)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".hs2cache", Path.GetFileName(path) + ".dox");

    public static T LoadCached<T>(string path)
    {
        byte[] source = File.ReadAllBytes(path);
        // Include the DTO assembly/schema and serializer version, not just file timestamps.
        byte[] identity = System.Text.Encoding.UTF8.GetBytes(typeof(T).AssemblyQualifiedName + "|" +
            typeof(T).Module.ModuleVersionId + "|" + typeof(REDox.DoxSerializer).Assembly.FullName);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(identity);
        hash.AppendData(source);
        byte[] sourceHash = hash.GetHashAndReset();
        string cachePath = CachePath(path);
        try
        {
            var info = new FileInfo(cachePath);
            if (info.Exists && info.Length > HeaderSize && info.Length <= MaxCacheBytes)
            {
                byte[] cache = File.ReadAllBytes(cachePath);
                if (cache.AsSpan(0, 8).SequenceEqual(CacheMagic) &&
                    cache.AsSpan(8, 32).SequenceEqual(sourceHash) &&
                    cache.AsSpan(40, 32).SequenceEqual(SHA256.HashData(cache.AsSpan(HeaderSize))))
                {
                    var result = REDox.DoxSerializer.Deserialize(cache[HeaderSize..], typeof(T), Settings);
                    if (result is T typed) return typed;
                }
            }
        }
        catch (Exception ex) when (IsCacheFailure(ex)) { }

        T value = ReadJson<T>(source);
        try
        {
            byte[] payload = REDox.DoxSerializer.Serialize(value!, typeof(T), Settings);
            if (payload.Length <= MaxCacheBytes - HeaderSize)
            {
                byte[] cache = new byte[HeaderSize + payload.Length];
                CacheMagic.CopyTo(cache);
                sourceHash.CopyTo(cache, 8);
                SHA256.HashData(payload).CopyTo(cache, 40);
                payload.CopyTo(cache, HeaderSize);
                WriteAtomic(cachePath, cache, keepBackup: false);
            }
        }
        catch (Exception ex) when (IsCacheFailure(ex)) { }
        return value;
    }

    private static bool IsCacheFailure(Exception ex) => ex is IOException or UnauthorizedAccessException
        or REDox.DocumentParseException or ArgumentException or InvalidOperationException or NotSupportedException;

    private static void WriteAtomic(string path, byte[] bytes, bool keepBackup)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (keepBackup && File.Exists(path))
                WriteAtomic(path + ".bak", File.ReadAllBytes(path), keepBackup: false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
