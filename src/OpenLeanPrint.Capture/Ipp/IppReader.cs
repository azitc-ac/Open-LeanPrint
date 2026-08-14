using System.Buffers.Binary;
using System.Text;

namespace OpenLeanPrint.Capture.Ipp;

/// <summary>
/// Parses the binary IPP wire format (RFC 8010) into an <see cref="IppMessage"/>.
/// </summary>
public static class IppReader
{
    public static IppMessage Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 8)
            throw new FormatException("IPP message too short for a header.");

        var msg = new IppMessage
        {
            VersionMajor = bytes[0],
            VersionMinor = bytes[1],
            OperationOrStatus = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2, 2)),
            RequestId = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4, 4)),
        };

        int pos = 8;
        IppAttributeGroup? group = null;

        // Pending attribute being accumulated (supports multi-valued attributes).
        string? pendingName = null;
        IppTag pendingTag = default;
        List<object>? pendingValues = null;

        void FlushPending()
        {
            if (pendingName is not null && group is not null)
                group.Add(new IppAttribute(pendingName, pendingTag, pendingValues!.ToArray()));
            pendingName = null;
            pendingValues = null;
        }

        while (pos < bytes.Length)
        {
            var tag = (IppTag)bytes[pos++];

            if (tag == IppTag.EndOfAttributes)
            {
                FlushPending();
                msg.Data = bytes.AsSpan(pos).ToArray();
                return msg;
            }

            if (IsDelimiter(tag))
            {
                FlushPending();
                group = msg.AddGroup(tag);
                continue;
            }

            // Value tag: name-length, name, value-length, value.
            if (pos + 2 > bytes.Length) throw new FormatException("Truncated name-length.");
            int nameLen = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos, 2));
            pos += 2;
            string name = Encoding.ASCII.GetString(bytes, pos, nameLen);
            pos += nameLen;

            if (pos + 2 > bytes.Length) throw new FormatException("Truncated value-length.");
            int valueLen = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(pos, 2));
            pos += 2;
            if (pos + valueLen > bytes.Length) throw new FormatException("Truncated value.");
            object value = DecodeValue(tag, bytes.AsSpan(pos, valueLen));
            pos += valueLen;

            if (nameLen == 0)
            {
                // Additional value of the current (pending) attribute.
                pendingValues ??= new List<object>();
                pendingValues.Add(value);
            }
            else
            {
                FlushPending();
                if (group is null)
                    throw new FormatException("Attribute value appeared before any group delimiter.");
                pendingName = name;
                pendingTag = tag;
                pendingValues = new List<object> { value };
            }
        }

        // No explicit end-of-attributes tag; flush what we have.
        FlushPending();
        return msg;
    }

    private static bool IsDelimiter(IppTag tag) => (byte)tag <= 0x05;

    private static object DecodeValue(IppTag tag, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case IppTag.Integer:
            case IppTag.Enum:
                return value.Length >= 4 ? BinaryPrimitives.ReadInt32BigEndian(value) : 0;

            case IppTag.Boolean:
                return value.Length >= 1 && value[0] != 0;

            case IppTag.TextWithoutLanguage:
            case IppTag.NameWithoutLanguage:
            case IppTag.Keyword:
            case IppTag.Uri:
            case IppTag.UriScheme:
            case IppTag.Charset:
            case IppTag.NaturalLanguage:
            case IppTag.MimeMediaType:
            case IppTag.MemberAttrName:
                return Encoding.UTF8.GetString(value);

            default:
                // octetString, dateTime, resolution, rangeOfInteger, collections,
                // and out-of-band tags are kept as raw bytes.
                return value.ToArray();
        }
    }
}
