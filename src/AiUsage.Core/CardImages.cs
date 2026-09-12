using System.Buffers.Binary;
using System.IO.Compression;

namespace AiUsage;

// Tiny PNGs avoid theme-dependent padding in Adaptive Card progress-bar containers.
// Everything is generated locally; no external asset requests or drawing dependencies.
public static class CardImages
{
    public static string Progress(double? value, bool weekly)
    {
        var p = value.HasValue && double.IsFinite(value.Value) ? Math.Clamp(value.Value, 0, 100) : 0;
        return Png(560, 8, (x, y) => x < 560 * p / 100
            ? weekly ? (112, 187, 123, 255) : (75, 163, 239, 255)
            : (140, 145, 155, 45));
    }
    public static string Dot(string hex)
    {
        var r = Convert.ToInt32(hex[1..3], 16); var g = Convert.ToInt32(hex[3..5], 16); var b = Convert.ToInt32(hex[5..7], 16);
        return Png(48, 48, (x, y) => (x - 23.5) * (x - 23.5) + (y - 23.5) * (y - 23.5) < 64 ? (r, g, b, 255) : (r, g, b, 25));
    }
    static string Png(int width, int height, Func<int, int, (int R, int G, int B, int A)> pixel)
    {
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 6;
        Chunk(png, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true)) {
            for (var y = 0; y < height; y++) {
                zlib.WriteByte(0);
                for (var x = 0; x < width; x++) { var c = pixel(x, y); zlib.Write([(byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A]); }
            }
        }
        Chunk(png, "IDAT", compressed.ToArray()); Chunk(png, "IEND", []);
        return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
    }
    static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> value = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(value, data.Length); output.Write(value);
        var name = System.Text.Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var bytes in new[] { name, data }) foreach (var b in bytes) {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(value, crc ^ 0xffffffff); output.Write(value);
    }
}
