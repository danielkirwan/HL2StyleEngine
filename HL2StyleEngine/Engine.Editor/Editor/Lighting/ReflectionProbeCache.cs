using Engine.Render;

namespace Engine.Editor.Editor;

public static class ReflectionProbeCache
{
    public static string PathFor(string levelPath, string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Probe ID must be a GUID.");
        return Path.ChangeExtension(Path.GetFullPath(levelPath), id + ".hs2reflection");
    }
    public static ReflectionEnvironmentData? Load(string path, string key)
    {
        try
        {
            using var file = File.OpenRead(path); using var reader = new BinaryReader(file);
            if (file.Length > 5 * 1024 * 1024 || reader.ReadUInt32() != 0x52533248 || reader.ReadInt32() != 1 ||
                !reader.ReadBytes(32).AsSpan().SequenceEqual(Convert.FromHexString(key))) return null;
            return ReflectionEnvironmentData.Read(file);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    public static void Save(string path, string key, ReflectionEnvironmentData data, CancellationToken cancel = default)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            using (var file = File.Create(temp))
            {
                using var writer = new BinaryWriter(file, System.Text.Encoding.UTF8, true);
                writer.Write(0x52533248u); writer.Write(1); writer.Write(Convert.FromHexString(key)); data.Write(file);
            }
            cancel.ThrowIfCancellationRequested(); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
