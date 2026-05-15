using System.Buffers.Binary;

namespace OAE.Core.Resources;

/// <summary>
/// Tiny PNG header parser. The first 8 bytes are the PNG signature; the IHDR
/// chunk follows immediately and starts with width + height as big-endian
/// uint32. Reading those is enough to surface dimensions in the Images tab
/// without taking a dependency on an image library.
/// </summary>
public static class PngReader
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// Returns <c>(width, height)</c> for a PNG file, or <c>null</c> if the
    /// file isn't readable as a PNG.
    /// </summary>
    public static (int Width, int Height)? TryReadDimensions(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> buffer = stackalloc byte[24];
            if (stream.Read(buffer) < 24) return null;
            for (var i = 0; i < PngSignature.Length; i++)
                if (buffer[i] != PngSignature[i]) return null;
            // IHDR chunk: bytes 8-11 length, 12-15 "IHDR", 16-19 width, 20-23 height.
            var w = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(16, 4));
            var h = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(20, 4));
            return ((int)w, (int)h);
        }
        catch
        {
            return null;
        }
    }
}
