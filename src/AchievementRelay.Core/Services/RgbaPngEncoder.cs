using System.Buffers.Binary;
using System.IO.Compression;

namespace AchievementRelay.Core.Services;

public static class RgbaPngEncoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width is <= 0 or > 2048)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height is <= 0 or > 2048)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var expectedLength = checked(width * height * 4);
        if (rgba.Length != expectedLength)
        {
            throw new ArgumentException("RGBA data length does not match the image dimensions.", nameof(rgba));
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR"u8, header);

        using var raw = new MemoryStream(checked((width * 4 + 1) * height));
        var rowBytes = width * 4;
        for (var row = 0; row < height; row++)
        {
            raw.WriteByte(0);
            raw.Write(rgba.Slice(row * rowBytes, rowBytes));
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT"u8, compressed.ToArray());
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(value, (uint)data.Length);
        output.Write(value);
        output.Write(type);
        output.Write(data);

        var crc = 0xffffffffu;
        foreach (var item in type)
        {
            crc = UpdateCrc(crc, item);
        }

        foreach (var item in data)
        {
            crc = UpdateCrc(crc, item);
        }

        BinaryPrimitives.WriteUInt32BigEndian(value, ~crc);
        output.Write(value);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }
}
