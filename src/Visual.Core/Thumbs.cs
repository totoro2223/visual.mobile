using System.IO.Compression;

namespace Visual.Core;

/// <summary>Scene thumbnails: W x H RGB, zlib + base64 inside the project (no image library); a BMP for the web panel.</summary>
public static class Thumbs
{
    public const int W = 80, H = 45;

    /// <summary>RGBA rows bottom-up (glReadPixels) to the stored text.</summary>
    public static string Pack(byte[] rgba)
    {
        var rgb = new byte[W * H * 3];
        for (int i = 0; i < W * H; i++) (rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]) = (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize)) z.Write(rgb);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>RGB rows bottom-up; null if it isn't a thumbnail.</summary>
    public static byte[]? Unpack(string? s)
    {
        if (s == null) return null;
        try
        {
            using var z = new ZLibStream(new MemoryStream(Convert.FromBase64String(s)), CompressionMode.Decompress);
            var rgb = new byte[W * H * 3];
            z.ReadExactly(rgb);
            return z.ReadByte() < 0 ? rgb : null;
        }
        catch (Exception e) when (e is FormatException or InvalidDataException or EndOfStreamException) { return null; }
    }

    /// <summary>24-bit BMP (bottom-up BGR, as the browsers show it). W * 3 is a multiple of 4: rows need no padding.</summary>
    public static byte[] Bmp(byte[] rgb)
    {
        var b = new byte[54 + rgb.Length];
        (b[0], b[1]) = ((byte)'B', (byte)'M');
        BitConverter.TryWriteBytes(b.AsSpan(2), b.Length);
        BitConverter.TryWriteBytes(b.AsSpan(10), 54);
        BitConverter.TryWriteBytes(b.AsSpan(14), 40);
        BitConverter.TryWriteBytes(b.AsSpan(18), W);
        BitConverter.TryWriteBytes(b.AsSpan(22), H);
        BitConverter.TryWriteBytes(b.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(b.AsSpan(28), (short)24);
        BitConverter.TryWriteBytes(b.AsSpan(34), rgb.Length);
        for (int i = 0; i < rgb.Length; i += 3) (b[54 + i], b[55 + i], b[56 + i]) = (rgb[i + 2], rgb[i + 1], rgb[i]);
        return b;
    }
}
