using System.Buffers.Binary;

namespace OpenLeanPrint.Capture.Ipp;

/// <summary>Helpers for encoding the less common IPP value types as raw bytes.</summary>
public static class IppValues
{
    /// <summary>
    /// Encodes an IPP <c>resolution</c> value: cross-feed (x) and feed (y)
    /// resolution as big-endian int32, then a units byte (3 = dpi, 4 = dpcm).
    /// </summary>
    public static byte[] Resolution(int x, int y, byte units = 3)
    {
        var b = new byte[9];
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0, 4), x);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4, 4), y);
        b[8] = units;
        return b;
    }
}
