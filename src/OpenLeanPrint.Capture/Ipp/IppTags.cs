namespace OpenLeanPrint.Capture.Ipp;

/// <summary>
/// IPP tags (RFC 8010). A single byte precedes every attribute group
/// (delimiter tags) and every attribute value (value tags).
/// </summary>
public enum IppTag : byte
{
    // Delimiter tags.
    OperationAttributes = 0x01,
    JobAttributes = 0x02,
    EndOfAttributes = 0x03,
    PrinterAttributes = 0x04,
    UnsupportedAttributes = 0x05,

    // Out-of-band value tags.
    Unsupported = 0x10,
    Unknown = 0x12,
    NoValue = 0x13,

    // Integer value tags.
    Integer = 0x21,
    Boolean = 0x22,
    Enum = 0x23,

    // octetString value tags.
    OctetString = 0x30,
    DateTime = 0x31,
    Resolution = 0x32,
    RangeOfInteger = 0x33,
    BegCollection = 0x34,
    TextWithLanguage = 0x35,
    NameWithLanguage = 0x36,
    EndCollection = 0x37,

    // character-string value tags.
    TextWithoutLanguage = 0x41,
    NameWithoutLanguage = 0x42,
    Keyword = 0x44,
    Uri = 0x45,
    UriScheme = 0x46,
    Charset = 0x47,
    NaturalLanguage = 0x48,
    MimeMediaType = 0x49,
    MemberAttrName = 0x4A,
}

/// <summary>IPP operation identifiers (subset LeanPrint needs). RFC 8011.</summary>
public enum IppOperation : short
{
    PrintJob = 0x0002,
    PrintUri = 0x0003,
    ValidateJob = 0x0004,
    CreateJob = 0x0005,
    SendDocument = 0x0006,
    SendUri = 0x0007,
    CancelJob = 0x0008,
    GetJobAttributes = 0x0009,
    GetJobs = 0x000A,
    GetPrinterAttributes = 0x000B,
}

/// <summary>IPP status codes (subset). RFC 8011.</summary>
public enum IppStatus : short
{
    SuccessfulOk = 0x0000,
    ClientErrorBadRequest = 0x0400,
    ClientErrorNotFound = 0x0406,
    ServerErrorInternalError = 0x0500,
    ServerErrorOperationNotSupported = 0x0501,
}

/// <summary>IPP job-state values (RFC 8011 §5.3.7).</summary>
public enum IppJobState
{
    Pending = 3,
    PendingHeld = 4,
    Processing = 5,
    Completed = 9,
}

/// <summary>IPP printer-state values (RFC 8011 §5.4.11).</summary>
public enum IppPrinterState
{
    Idle = 3,
    Processing = 4,
    Stopped = 5,
}
