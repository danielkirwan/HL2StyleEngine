using System.Security.Cryptography;

namespace Engine.Render;

public enum ReflectionPreset { None = 0, SyntheticStudio = 1 }

// v1: linear RGBA16F, D3D +X/-X/+Y/-Y/+Z/-Z faces, perceptual r=mip/(mips-1),
// alpha=max(.04,r)^2, correlated Smith GGX, Schlick split sum F0*A+B (RG16F LUT).
public sealed record ReflectionEnvironmentData(int Size, int MipCount, int LutSize, byte[][] Faces, byte[] Lut)
{
    public const int Version = 1;
    public long PayloadBytes => Faces.Sum(f => (long)f.Length) + Lut.Length;

    public void Validate()
    {
        if (Size is < 1 or > 256 || (Size & (Size-1)) != 0 || MipCount != 1+(int)Math.Log2(Size) ||
            LutSize is < 1 or > 256 || Faces.Length != 6*MipCount || Lut.Length != LutSize*LutSize*4)
            throw new InvalidDataException("Unsupported reflection dimensions.");
        for (int mip=0; mip<MipCount; mip++) for (int face=0; face<6; face++)
            if (Faces[mip*6+face].Length != Math.Max(1,Size>>mip)*Math.Max(1,Size>>mip)*8)
                throw new InvalidDataException("Invalid reflection face size.");
        foreach (var bytes in Faces.Append(Lut)) for (int i=0;i<bytes.Length;i+=2)
        {
            float value = (float)BitConverter.UInt16BitsToHalf(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i,2)));
            if (!float.IsFinite(value) || value < 0) throw new InvalidDataException("Invalid reflection texel.");
        }
    }

    public void Write(string path)
    {
        string temp = path+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            using (var stream = File.Create(temp)) Write(stream);
            File.Move(temp,path,true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Write(Stream stream)
    {
        Validate();
        using var payload = new MemoryStream();
        using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Size); writer.Write(MipCount); writer.Write(LutSize);
            foreach (var face in Faces) writer.Write(face);
            writer.Write(Lut);
        }
        byte[] bytes = payload.ToArray();
        using var output = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        output.Write(0x45533248); output.Write(Version); output.Write(bytes.Length); output.Write(SHA256.HashData(bytes)); output.Write(bytes);
    }

    public static ReflectionEnvironmentData Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        if (reader.ReadInt32()!=0x45533248 || reader.ReadInt32()!=Version) throw new InvalidDataException("Reflection filter version mismatch.");
        int length=reader.ReadInt32();
        if (length is < 12 or > 5*1024*1024) throw new InvalidDataException("Reflection payload exceeds bounds.");
        byte[] hash=reader.ReadBytes(32), bytes=reader.ReadBytes(length);
        if (bytes.Length!=length || !SHA256.HashData(bytes).AsSpan().SequenceEqual(hash) || stream.ReadByte()!=-1)
            throw new InvalidDataException("Reflection payload checksum/length mismatch.");
        using var data=new BinaryReader(new MemoryStream(bytes));
        int size=data.ReadInt32(), mips=data.ReadInt32(), lutSize=data.ReadInt32();
        if (size is < 1 or > 256 || (size & (size-1))!=0 || mips!=1+(int)Math.Log2(size) || lutSize is < 1 or > 256)
            throw new InvalidDataException("Unsupported reflection dimensions.");
        var faces=new byte[mips*6][];
        for(int mip=0;mip<mips;mip++) for(int face=0;face<6;face++) faces[mip*6+face]=data.ReadBytes(Math.Max(1,size>>mip)*Math.Max(1,size>>mip)*8);
        var result=new ReflectionEnvironmentData(size,mips,lutSize,faces,data.ReadBytes(lutSize*lutSize*4));
        if(data.BaseStream.Position!=length) throw new InvalidDataException("Reflection payload trailing data.");
        result.Validate(); return result;
    }
}
