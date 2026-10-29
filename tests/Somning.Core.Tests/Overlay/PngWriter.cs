using System.IO.Compression;

namespace Somning.Core.Tests.Overlay;

/// <summary>Minimal RGB PNG encoder (stored via ZLibStream) for test previews.</summary>
public static class PngWriter
{
    // Premultiplied BGRA over a flat backdrop (with a faint gradient so translucency is visible).
    public static void Composite(byte[] target, int targetWidth, int offX, int offY, uint[] tile, int w, int h, (int R, int G, int B) bg)
    {
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var p = tile[y * w + x];
                var a = (p >> 24) / 255f;
                var shade = 1f + 0.10f * ((x + y) % 40 < 20 ? 1f : -1f) * 0.5f;
                var br = Math.Clamp(bg.R * shade, 0, 255);
                var bgc = Math.Clamp(bg.G * shade, 0, 255);
                var bb = Math.Clamp(bg.B * shade, 0, 255);
                var r = ((p >> 16) & 0xFF) + br * (1 - a);
                var g = ((p >> 8) & 0xFF) + bgc * (1 - a);
                var b = (p & 0xFF) + bb * (1 - a);
                var i = ((offY + y) * targetWidth + offX + x) * 3;
                target[i] = (byte)Math.Clamp(r, 0, 255);
                target[i + 1] = (byte)Math.Clamp(g, 0, 255);
                target[i + 2] = (byte)Math.Clamp(b, 0, 255);
            }
        }
    }


    public static void WriteRgb(string path, byte[] rgb, int width, int height)
    {
        using var stream = File.Create(path);
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBe(header, 0, width);
        WriteBe(header, 4, height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        Chunk(stream, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgb, y * width * 3, width * 3);
            }
        }

        Chunk(stream, "IDAT", raw.ToArray());
        Chunk(stream, "IEND", []);
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        stream.Write(len);
        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        data.CopyTo(body, 4);
        stream.Write(body);
        var crc = new byte[4];
        WriteBe(crc, 0, (int)Crc32(body));
        stream.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }

    private static void WriteBe(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
