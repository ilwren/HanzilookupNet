namespace HanziLookup;

/// <summary>
/// Decodes the base64 encoded, compactly packed sub-stroke table of a character data file.
/// </summary>
/// <remarks>
/// <para>
/// Three bytes per sub-stroke: direction, length, and a nibble packed centre
/// (<c>high nibble = x, low nibble = y</c> where 0 means "no centre").
/// </para>
/// <para>
/// This is a port of <c>decodeCompact()</c> from the JavaScript implementation. It intentionally
/// keeps two properties of the original: padding characters are treated as zeroes, and writing past
/// the computed buffer length is a no-op (in JavaScript those writes go into a typed array and are
/// silently dropped). It is more forgiving than the original in one respect: an unpadded input whose
/// length is not a multiple of four is supported as well.
/// </para>
/// </remarks>
public static class CompactDataDecoder
{
    private const string Base64Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    /// <summary>Decodes a base64 (or "base64url" style, unpadded) compact sub-stroke string into bytes.</summary>
    public static byte[] Decode(string base64)
    {
        ArgumentNullException.ThrowIfNull(base64);

        var lookup = BuildLookupTable();

        var bufferLength = (int)(base64.Length * 0.75);
        if (base64.Length > 0 && base64[base64.Length - 1] == '=')
        {
            bufferLength--;
            if (base64.Length > 1 && base64[base64.Length - 2] == '=')
            {
                bufferLength--;
            }
        }

        if (bufferLength <= 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[bufferLength];
        var p = 0;
        for (var i = 0; i < base64.Length; i += 4)
        {
            var encoded1 = Lookup(lookup, base64, i);
            var encoded2 = Lookup(lookup, base64, i + 1);
            var encoded3 = Lookup(lookup, base64, i + 2);
            var encoded4 = Lookup(lookup, base64, i + 3);

            Write(buffer, p++, (byte)((encoded1 << 2) | (encoded2 >> 4)));
            Write(buffer, p++, (byte)(((encoded2 & 15) << 4) | (encoded3 >> 2)));
            Write(buffer, p++, (byte)(((encoded3 & 3) << 6) | (encoded4 & 63)));
        }

        return buffer;
    }

    private static byte[] BuildLookupTable()
    {
        var lookup = new byte[256];
        for (var i = 0; i < Base64Alphabet.Length; i++)
        {
            lookup[Base64Alphabet[i]] = (byte)i;
        }

        return lookup;
    }

    private static int Lookup(byte[] lookup, string base64, int index)
    {
        // JavaScript: charCodeAt() returns NaN past the end, and indexing a Uint8Array with it
        // yields undefined, which behaves like 0 in the bit arithmetic below.
        if ((uint)index >= (uint)base64.Length)
        {
            return 0;
        }

        var code = base64[index];
        return code < 256 ? lookup[code] : 0;
    }

    private static void Write(byte[] buffer, int index, byte value)
    {
        if ((uint)index < (uint)buffer.Length)
        {
            buffer[index] = value;
        }
    }
}
