using System.Buffers.Binary;

namespace Engine.Runtime.Audio;

// Strict authored PCM16 WAV input. Parsing and conversion run on the control worker,
// never the device callback. Chunk padding/ordering is accepted; malformed files aren't.
public sealed class PcmWave : IDisposable
{
    private readonly FileStream file;
    private readonly long dataStart;
    private long remaining;
    private readonly byte[] bytes = new byte[32768];
    public int Channels { get; }
    public int Rate { get; }
    public long Frames { get; }
    public PcmWave(string path)
    {
        file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        try
        {
            using var reader = new BinaryReader(file, System.Text.Encoding.ASCII, true);
            if (reader.ReadUInt32() != 0x46464952 || reader.ReadUInt32() != file.Length - 8 || reader.ReadUInt32() != 0x45564157)
                throw new InvalidDataException("Invalid RIFF WAV: " + path);
            int format = 0, bits = 0, align = 0; long size = 0;
            while (file.Position + 8 <= file.Length)
            {
                uint id = reader.ReadUInt32(), length = reader.ReadUInt32();
                long end = file.Position + length;
                if (end > file.Length) throw new InvalidDataException("Truncated WAV chunk.");
                if (id == 0x20746d66)
                {
                    if (length < 16) throw new InvalidDataException("Invalid WAV format.");
                    format = reader.ReadUInt16(); Channels = reader.ReadUInt16(); Rate = reader.ReadInt32();
                    reader.ReadInt32(); align = reader.ReadUInt16(); bits = reader.ReadUInt16();
                }
                if (id == 0x61746164) { dataStart = file.Position; size = length; }
                file.Position = end + (length & 1);
            }
            if (format != 1 || bits != 16 || Channels is not (1 or 2) || Rate is < 8000 or > 96000 ||
                align != Channels * 2 || size <= 0 || size % align != 0 || size > 512 * 1024 * 1024)
                throw new InvalidDataException("Expected mono/stereo PCM16 WAV, 8-96 kHz, <=512 MiB: " + path);
            Frames = size / align; Rewind();
        }
        catch { file.Dispose(); throw; }
    }
    public void Rewind() { file.Position = dataStart; remaining = Frames * Channels * 2; }
    public int Read(float[] destination, int frames)
    {
        int count = (int)Math.Min(remaining, Math.Min(bytes.Length, frames * Channels * 2));
        file.ReadExactly(bytes.AsSpan(0, count)); remaining -= count;
        for (int i = 0; i < count / 2; ++i) destination[i] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / 32768f;
        return count / (2 * Channels);
    }
    public float[] ReadAll()
    {
        if (Frames > Rate * 10L) throw new InvalidDataException("Resident sample exceeds 10 seconds.");
        var output = new float[checked((int)(Frames * Channels))];
        var block = new float[16384]; int offset = 0, read;
        while ((read = Read(block, block.Length / Channels)) > 0)
        { block.AsSpan(0, read * Channels).CopyTo(output.AsSpan(offset)); offset += read * Channels; }
        return output;
    }
    public void Dispose() => file.Dispose();
}
