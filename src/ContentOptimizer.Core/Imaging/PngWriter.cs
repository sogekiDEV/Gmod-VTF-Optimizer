using System.Buffers.Binary;
using System.IO.Compression;

namespace ContentOptimizer.Core.Imaging;

public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in type)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        foreach (byte b in data)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        stream.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(t);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc(t, data));
        stream.Write(len);
    }

    public static void Write(string path, ReadOnlySpan<byte> rgba, int width, int height)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(fs, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            var row = new byte[width * 4 + 1];
            for (int y = 0; y < height; y++)
            {
                row[0] = 0;
                rgba.Slice(y * width * 4, width * 4).CopyTo(row.AsSpan(1));
                z.Write(row);
            }
        }

        WriteChunk(fs, "IDAT", compressed.ToArray());
        WriteChunk(fs, "IEND", ReadOnlySpan<byte>.Empty);
    }
}
