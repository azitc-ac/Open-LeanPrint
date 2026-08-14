using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using OpenLeanPrint.Capture;
using OpenLeanPrint.Capture.Ipp;
using OpenLeanPrint.Capture.Server;
using Xunit;

namespace OpenLeanPrint.Capture.Tests;

public class IppServerTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task<IppMessage> PostAsync(string prefix, IppMessage request)
    {
        using var client = new HttpClient();
        using var content = new ByteArrayContent(IppWriter.Serialize(request));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/ipp");
        var response = await client.PostAsync(prefix.TrimEnd('/') + "/leanprint", content);
        response.EnsureSuccessStatusCode();
        return IppReader.Parse(await response.Content.ReadAsByteArrayAsync());
    }

    private static IppMessage NewRequest(IppOperation op, string printerUri)
    {
        var msg = new IppMessage { OperationOrStatus = (short)op, RequestId = 1 };
        var g = msg.AddGroup(IppTag.OperationAttributes);
        g.Add(new IppAttribute("attributes-charset", IppTag.Charset, "utf-8"));
        g.Add(new IppAttribute("attributes-natural-language", IppTag.NaturalLanguage, "en"));
        g.Add(new IppAttribute("printer-uri", IppTag.Uri, printerUri));
        return msg;
    }

    [Fact]
    public async Task GetPrinterAttributes_ReturnsOkWithPrinterName()
    {
        var options = new IppPrinterOptions { Port = FreePort(), PrinterName = "OLP-Test" };
        using var server = new IppPrinterServer(options);
        server.Start();

        var response = await PostAsync(options.HttpPrefix, NewRequest(IppOperation.GetPrinterAttributes, options.PrinterUri));

        Assert.Equal(IppStatus.SuccessfulOk, response.Status);
        var printer = response.FirstGroup(IppTag.PrinterAttributes);
        Assert.NotNull(printer);
        Assert.Equal("OLP-Test", printer!.Find("printer-name")!.AsString());
        Assert.Equal("application/pdf", printer.Find("document-format-default")!.AsString());

        await server.StopAsync();
    }

    [Fact]
    public async Task GetPrinterAttributes_AdvertisesIppEverywhereRequiredAttributes()
    {
        var options = new IppPrinterOptions { Port = FreePort() };
        using var server = new IppPrinterServer(options);
        server.Start();

        var response = await PostAsync(options.HttpPrefix, NewRequest(IppOperation.GetPrinterAttributes, options.PrinterUri));
        var printer = response.FirstGroup(IppTag.PrinterAttributes)!;

        // A representative slice of the attributes the Windows IPP class driver
        // needs before it will accept a driverless queue.
        foreach (var name in new[]
        {
            "ipp-features-supported", "printer-uuid", "printer-device-id",
            "print-color-mode-supported", "media-ready", "copies-supported",
            "finishings-supported", "urf-supported", "pwg-raster-document-type-supported",
            "job-creation-attributes-supported",
        })
        {
            Assert.NotNull(printer.Find(name));
        }

        Assert.Equal("ipp-everywhere", printer.Find("ipp-features-supported")!.AsString());
        Assert.StartsWith("urn:uuid:", printer.Find("printer-uuid")!.AsString());

        await server.StopAsync();
    }

    [Fact]
    public async Task PrintJob_CapturesPdfAndParsesPages()
    {
        var options = new IppPrinterOptions { Port = FreePort() };
        using var server = new IppPrinterServer(options);

        var tcs = new TaskCompletionSource<CapturedJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.JobCaptured += (_, job) => tcs.TrySetResult(job);
        server.Start();

        byte[] pdf = TestPdfs.WithPages((595, 842), (595, 842), (612, 792));

        var request = NewRequest(IppOperation.PrintJob, options.PrinterUri);
        var op = request.FirstGroup(IppTag.OperationAttributes)!;
        op.Add(new IppAttribute("job-name", IppTag.NameWithoutLanguage, "Hello.pdf"));
        op.Add(new IppAttribute("requesting-user-name", IppTag.NameWithoutLanguage, "tester"));
        op.Add(new IppAttribute("document-format", IppTag.MimeMediaType, "application/pdf"));
        request.Data = pdf;

        var response = await PostAsync(options.HttpPrefix, request);
        Assert.Equal(IppStatus.SuccessfulOk, response.Status);
        Assert.NotNull(response.FirstGroup(IppTag.JobAttributes)!.Find("job-id")!.AsInt());

        var captured = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Hello.pdf", captured.JobName);
        Assert.Equal("tester", captured.UserName);
        Assert.True(captured.IsPdf);
        Assert.Equal(pdf.Length, captured.Data.Length);
        Assert.Null(captured.ParseError);
        Assert.NotNull(captured.Document);
        Assert.Equal(3, captured.Document!.PageCount);
        Assert.Equal(595, captured.Document.PageSizes[0].Width, 0);

        await server.StopAsync();
    }

    [Fact]
    public async Task CreateJobThenSendDocument_CapturesOnLastDocument()
    {
        var options = new IppPrinterOptions { Port = FreePort() };
        using var server = new IppPrinterServer(options);

        var tcs = new TaskCompletionSource<CapturedJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.JobCaptured += (_, job) => tcs.TrySetResult(job);
        server.Start();

        // Create-Job
        var create = NewRequest(IppOperation.CreateJob, options.PrinterUri);
        create.FirstGroup(IppTag.OperationAttributes)!
              .Add(new IppAttribute("job-name", IppTag.NameWithoutLanguage, "Chunked.pdf"));
        var createResp = await PostAsync(options.HttpPrefix, create);
        int jobId = createResp.FirstGroup(IppTag.JobAttributes)!.Find("job-id")!.AsInt()!.Value;
        Assert.True(jobId > 0);

        // Send-Document (last)
        byte[] pdf = TestPdfs.WithPages((595, 842));
        var send = NewRequest(IppOperation.SendDocument, options.PrinterUri);
        var sop = send.FirstGroup(IppTag.OperationAttributes)!;
        sop.Add(new IppAttribute("job-id", IppTag.Integer, jobId));
        sop.Add(new IppAttribute("document-format", IppTag.MimeMediaType, "application/pdf"));
        sop.Add(new IppAttribute("last-document", IppTag.Boolean, true));
        send.Data = pdf;
        var sendResp = await PostAsync(options.HttpPrefix, send);
        Assert.Equal(IppStatus.SuccessfulOk, sendResp.Status);

        var captured = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Chunked.pdf", captured.JobName);
        Assert.Equal(1, captured.Document!.PageCount);

        await server.StopAsync();
    }
}
