using System.IO.Compression;

namespace Somning.OverlayPreview;

/// <summary>Minimal opaque RGB PNG encoder (no dependencies).</summary>
internal static class PngWriter
{
    public static void WriteRgb(string path, int width, int height, ReadOnlySpan<byte> rgb)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgb.Slice(y * width * 3, width * 3));
            }
        }

        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        WriteBe(header, 0, (uint)width);
        WriteBe(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBe(crcBytes, 0, Crc(Crc(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF);
        s.Write(crcBytes);
    }

    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static void WriteBe(Span<byte> span, int offset, uint value)
    {
        span[offset] = (byte)(value >> 24);
        span[offset + 1] = (byte)(value >> 16);
        span[offset + 2] = (byte)(value >> 8);
        span[offset + 3] = (byte)value;
    }
}
