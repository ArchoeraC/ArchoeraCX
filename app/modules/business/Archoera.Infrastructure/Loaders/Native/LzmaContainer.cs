using System.Buffers.Binary;
using SevenZip.Compression.LZMA;

namespace Archoera.Infrastructure.Loaders.Native;

/// <summary>
/// Decodes the raw LZMA stream binarypatcher uses for its <c>.lzma</c> files:
/// the 13-byte LZMA-alone header (properties, dictionary size, uncompressed
/// size) followed by the compressed data.
/// </summary>
internal static class LzmaContainer
{
    private const int HeaderLength = 13;

    public static byte[] Decompress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderLength)
        {
            throw new InvalidDataException("LZMA stream is shorter than its header");
        }

        var properties = data.AsSpan(0, 5).ToArray();
        var outputSize = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(5, 8));
        using var input = new MemoryStream(data, HeaderLength, data.Length - HeaderLength);
        using var output = new MemoryStream();
        var decoder = new Decoder();
        decoder.SetDecoderProperties(properties);
        decoder.Code(input, output, input.Length, outputSize, null);
        return output.ToArray();
    }
}
