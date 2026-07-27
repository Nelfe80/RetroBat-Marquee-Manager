using System.Buffers.Binary;
using System.IO;
using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// Reads image pixel dimensions from the file HEADER only — never a full decode
/// (spec §27). Covers PNG / JPEG / GIF / BMP / WEBP(VP8/VP8L/VP8X). Returns null
/// for anything it cannot read cheaply; the caller degrades to "unreadable".
/// </summary>
public static class SetupImageDimensions
{
    public static PixelSize? Read(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[32];
            int read = stream.Read(head);
            if (read < 16) return null;

            // PNG: 8-byte signature, then IHDR width@16 height@20 (big-endian).
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
                return new PixelSize(
                    (int)BinaryPrimitives.ReadUInt32BigEndian(head.Slice(16, 4)),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(head.Slice(20, 4)));

            // GIF: "GIF", width@6 height@8 (little-endian uint16).
            if (head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F')
                return new PixelSize(
                    BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(6, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(8, 2)));

            // BMP: "BM", width@18 height@22 (little-endian int32).
            if (head[0] == (byte)'B' && head[1] == (byte)'M')
                return new PixelSize(
                    BinaryPrimitives.ReadInt32LittleEndian(head.Slice(18, 4)),
                    Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(head.Slice(22, 4))));

            // WEBP: "RIFF"...."WEBP".
            if (head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
                && head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P')
                return ReadWebp(head);

            // JPEG: scan SOF markers.
            if (head[0] == 0xFF && head[1] == 0xD8)
                return ReadJpeg(stream);
        }
        catch
        {
            // unreadable header
        }
        return null;
    }

    private static PixelSize? ReadWebp(ReadOnlySpan<byte> head)
    {
        // VP8X: canvas width-1 @24 (24-bit LE), height-1 @27.
        if (head[12] == (byte)'V' && head[13] == (byte)'P' && head[14] == (byte)'8' && head[15] == (byte)'X')
        {
            int w = 1 + (head[24] | head[25] << 8 | head[26] << 16);
            int h = 1 + (head[27] | head[28] << 8 | head[29] << 16);
            return new PixelSize(w, h);
        }
        // Lossy VP8: 16-bit dimensions @26/@28 (14 low bits).
        if (head[12] == (byte)'V' && head[13] == (byte)'P' && head[14] == (byte)'8' && head[15] == (byte)' ')
        {
            int w = (head[26] | head[27] << 8) & 0x3FFF;
            int h = (head[28] | head[29] << 8) & 0x3FFF;
            return new PixelSize(w, h);
        }
        return null;
    }

    private static PixelSize? ReadJpeg(Stream stream)
    {
        stream.Position = 2;
        Span<byte> two = stackalloc byte[2];
        while (stream.Read(two) == 2)
        {
            if (two[0] != 0xFF) return null;
            byte marker = two[1];
            // standalone markers without a length payload
            if (marker is 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) continue;
            if (stream.Read(two) != 2) return null;
            int length = (two[0] << 8) | two[1];
            // SOF0..SOF15, excluding DHT(C4)/JPG(C8)/DAC(CC)
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                Span<byte> sof = stackalloc byte[5]; // precision + height(2) + width(2)
                if (stream.Read(sof) != 5) return null;
                int height = (sof[1] << 8) | sof[2];
                int width = (sof[3] << 8) | sof[4];
                return new PixelSize(width, height);
            }
            stream.Position += length - 2;
        }
        return null;
    }
}
