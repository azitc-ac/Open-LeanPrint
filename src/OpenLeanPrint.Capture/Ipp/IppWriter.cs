using System.Buffers.Binary;
using System.Text;

namespace OpenLeanPrint.Capture.Ipp;

/// <summary>
/// Serialises an <see cref="IppMessage"/> to the binary IPP wire format (RFC 8010).
/// </summary>
public static class IppWriter
{
    public static byte[] Serialize(IppMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);
        using var ms = new MemoryStream();

        ms.WriteByte(msg.VersionMajor);
        ms.WriteByte(msg.VersionMinor);
        WriteInt16(ms, msg.OperationOrStatus);
        WriteInt32(ms, msg.RequestId);

        foreach (var group in msg.Groups)
        {
            ms.WriteByte((byte)group.GroupTag);
            foreach (var attr in group.Attributes)
                WriteAttribute(ms, attr);
        }

        ms.WriteByte((byte)IppTag.EndOfAttributes);

        if (msg.Data.Length > 0)
            ms.Write(msg.Data, 0, msg.Data.Length);

        return ms.ToArray();
    }

    private static void WriteAttribute(Stream s, IppAttribute attr)
    {
        // An attribute with no values is not representable on the wire; skip it.
        if (attr.Values.Count == 0)
            return;

        for (int i = 0; i < attr.Values.Count; i++)
        {
            s.WriteByte((byte)attr.Tag);

            // First value carries the name; additional values use a zero-length name.
            string name = i == 0 ? attr.Name : string.Empty;
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            WriteInt16(s, (short)nameBytes.Length);
            s.Write(nameBytes, 0, nameBytes.Length);

            byte[] valueBytes = EncodeValue(attr.Tag, attr.Values[i]);
            WriteInt16(s, (short)valueBytes.Length);
            s.Write(valueBytes, 0, valueBytes.Length);
        }
    }

    private static byte[] EncodeValue(IppTag tag, object value)
    {
        switch (value)
        {
            case int i:
            {
                var b = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(b, i);
                return b;
            }
            case bool flag:
                return new[] { (byte)(flag ? 1 : 0) };
            case string str:
                return Encoding.UTF8.GetBytes(str);
            case byte[] raw:
                return raw;
            default:
                throw new NotSupportedException(
                    $"Cannot encode value of type {value.GetType()} for tag {tag}.");
        }
    }

    private static void WriteInt16(Stream s, short v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        s.WriteByte(b[0]);
        s.WriteByte(b[1]);
    }

    private static void WriteInt32(Stream s, int v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b);
    }
}
