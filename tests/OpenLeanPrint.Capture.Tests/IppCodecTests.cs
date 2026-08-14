using OpenLeanPrint.Capture.Ipp;
using Xunit;

namespace OpenLeanPrint.Capture.Tests;

public class IppCodecTests
{
    [Fact]
    public void RoundTrip_PreservesHeaderGroupsAndTypedValues()
    {
        var msg = new IppMessage
        {
            VersionMajor = 1,
            VersionMinor = 1,
            OperationOrStatus = (short)IppOperation.PrintJob,
            RequestId = 42,
        };
        var op = msg.AddGroup(IppTag.OperationAttributes);
        op.Add(new IppAttribute("attributes-charset", IppTag.Charset, "utf-8"));
        op.Add(new IppAttribute("attributes-natural-language", IppTag.NaturalLanguage, "en"));
        op.Add(new IppAttribute("job-name", IppTag.NameWithoutLanguage, "Report.pdf"));
        op.Add(new IppAttribute("copies", IppTag.Integer, 3));
        op.Add(new IppAttribute("collate", IppTag.Boolean, true));

        byte[] wire = IppWriter.Serialize(msg);
        var parsed = IppReader.Parse(wire);

        Assert.Equal(1, parsed.VersionMajor);
        Assert.Equal((short)IppOperation.PrintJob, parsed.OperationOrStatus);
        Assert.Equal(42, parsed.RequestId);

        var pop = parsed.FirstGroup(IppTag.OperationAttributes);
        Assert.NotNull(pop);
        Assert.Equal("utf-8", pop!.Find("attributes-charset")!.AsString());
        Assert.Equal("Report.pdf", pop.Find("job-name")!.AsString());
        Assert.Equal(3, pop.Find("copies")!.AsInt());
        Assert.Equal(true, pop.Find("collate")!.Values[0]);
    }

    [Fact]
    public void RoundTrip_PreservesMultiValuedAttribute()
    {
        var msg = new IppMessage { OperationOrStatus = (short)IppOperation.GetPrinterAttributes, RequestId = 7 };
        var op = msg.AddGroup(IppTag.OperationAttributes);
        op.Add(new IppAttribute("attributes-charset", IppTag.Charset, "utf-8"));
        op.Add(new IppAttribute("attributes-natural-language", IppTag.NaturalLanguage, "en"));
        var printer = msg.AddGroup(IppTag.PrinterAttributes);
        printer.Add(new IppAttribute("ipp-versions-supported", IppTag.Keyword, "1.1", "2.0"));

        var parsed = IppReader.Parse(IppWriter.Serialize(msg));

        var attr = parsed.FirstGroup(IppTag.PrinterAttributes)!.Find("ipp-versions-supported")!;
        Assert.Equal(2, attr.Values.Count);
        Assert.Equal(new object[] { "1.1", "2.0" }, attr.Values);
    }

    [Fact]
    public void RoundTrip_PreservesTrailingDocumentData()
    {
        var msg = new IppMessage { OperationOrStatus = (short)IppOperation.PrintJob, RequestId = 1 };
        var op = msg.AddGroup(IppTag.OperationAttributes);
        op.Add(new IppAttribute("attributes-charset", IppTag.Charset, "utf-8"));
        op.Add(new IppAttribute("attributes-natural-language", IppTag.NaturalLanguage, "en"));
        msg.Data = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

        var parsed = IppReader.Parse(IppWriter.Serialize(msg));

        Assert.Equal(msg.Data, parsed.Data);
    }

    [Fact]
    public void Response_StatusCodeIsExposedTypedly()
    {
        var msg = new IppMessage { OperationOrStatus = (short)IppStatus.SuccessfulOk, RequestId = 1 };
        var parsed = IppReader.Parse(IppWriter.Serialize(msg));
        Assert.Equal(IppStatus.SuccessfulOk, parsed.Status);
    }
}
