namespace OpenLeanPrint.Capture.Ipp;

/// <summary>
/// A single IPP attribute: a name, its value tag, and one or more values.
/// Values are stored as <see cref="int"/>, <see cref="bool"/>,
/// <see cref="string"/> or <see cref="byte"/>[] depending on the tag.
/// </summary>
public sealed class IppAttribute
{
    public IppAttribute(string name, IppTag tag, params object[] values)
    {
        Name = name;
        Tag = tag;
        Values = values.Length == 0 ? Array.Empty<object>() : values.ToArray();
    }

    public string Name { get; }
    public IppTag Tag { get; }
    public IReadOnlyList<object> Values { get; }

    /// <summary>First value as string (invariant), or null if there are none.</summary>
    public string? AsString() => Values.Count > 0 ? Convert.ToString(Values[0], System.Globalization.CultureInfo.InvariantCulture) : null;

    /// <summary>First value as int, or null if none / not an integer.</summary>
    public int? AsInt() => Values.Count > 0 && Values[0] is int i ? i : null;
}

/// <summary>A delimiter-tagged group of attributes (operation, job or printer).</summary>
public sealed class IppAttributeGroup
{
    public IppAttributeGroup(IppTag groupTag) => GroupTag = groupTag;

    public IppTag GroupTag { get; }
    public List<IppAttribute> Attributes { get; } = new();

    public IppAttribute? Find(string name) =>
        Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));

    public IppAttributeGroup Add(IppAttribute attribute)
    {
        Attributes.Add(attribute);
        return this;
    }
}

/// <summary>
/// An IPP message — used for both requests and responses. For a request,
/// <see cref="OperationOrStatus"/> holds the operation-id; for a response it
/// holds the status-code. <see cref="Data"/> carries any document payload that
/// follows the attributes (e.g. the PDF in a Print-Job).
/// </summary>
public sealed class IppMessage
{
    public byte VersionMajor { get; set; } = 1;
    public byte VersionMinor { get; set; } = 1;

    /// <summary>operation-id (request) or status-code (response).</summary>
    public short OperationOrStatus { get; set; }

    public int RequestId { get; set; }

    public List<IppAttributeGroup> Groups { get; } = new();

    /// <summary>Document data following the attributes, if any.</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public IppOperation Operation => (IppOperation)OperationOrStatus;
    public IppStatus Status => (IppStatus)OperationOrStatus;

    public IppAttributeGroup? FirstGroup(IppTag groupTag) =>
        Groups.FirstOrDefault(g => g.GroupTag == groupTag);

    /// <summary>Finds an attribute by name across all groups (first match).</summary>
    public IppAttribute? FindAttribute(string name)
    {
        foreach (var g in Groups)
        {
            var a = g.Find(name);
            if (a is not null) return a;
        }
        return null;
    }

    public IppAttributeGroup AddGroup(IppTag groupTag)
    {
        var g = new IppAttributeGroup(groupTag);
        Groups.Add(g);
        return g;
    }
}
