using System.Collections.Concurrent;
using System.Net;
using OpenLeanPrint.Capture.Ipp;
using OpenLeanPrint.Capture.Pdf;

namespace OpenLeanPrint.Capture.Server;

/// <summary>Configuration for a <see cref="IppPrinterServer"/>.</summary>
public sealed record IppPrinterOptions
{
    /// <summary>The printer name advertised to the OS.</summary>
    public string PrinterName { get; init; } = "OpenLeanPrint";

    /// <summary>TCP port the loopback HTTP/IPP endpoint listens on.</summary>
    public int Port { get; init; } = 6310;

    /// <summary>Resource path of the print queue (the tail of the printer URI).</summary>
    public string ResourcePath { get; init; } = "leanprint";

    /// <summary>The ipp:// URI advertised as printer-uri-supported.</summary>
    public string PrinterUri => $"ipp://localhost:{Port}/{ResourcePath}";

    /// <summary>The http:// prefix the listener binds to.</summary>
    public string HttpPrefix => $"http://localhost:{Port}/";
}

/// <summary>
/// A minimal loopback IPP print service. The OS is pointed at it via the in-box
/// IPP class driver (no third-party driver); when an application prints, the job
/// arrives here as PDF and is surfaced through <see cref="JobCaptured"/>.
/// <para>
/// Supports the operations Windows needs to discover the queue and submit a job:
/// Get-Printer-Attributes, Validate-Job, Print-Job, and the
/// Create-Job / Send-Document pair.
/// </para>
/// </summary>
public sealed class IppPrinterServer : IDisposable
{
    private readonly IppPrinterOptions _options;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<int, PendingJob> _pendingJobs = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    // Stable UUID advertised as printer-uuid (IPP Everywhere requires one).
    private const string PrinterUuid = "urn:uuid:6f4c6e50-7072-696e-7420-4f70656e4c50";
    private int _nextJobId;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public IppPrinterServer(IppPrinterOptions? options = null)
    {
        _options = options ?? new IppPrinterOptions();
        _listener.Prefixes.Add(_options.HttpPrefix);
    }

    /// <summary>Raised on the thread pool whenever a complete job has been captured.</summary>
    public event EventHandler<CapturedJob>? JobCaptured;

    /// <summary>
    /// Raised for every incoming HTTP/IPP request with a short diagnostic line
    /// (method, path, byte count, parsed operation or parse error). Useful for
    /// seeing exactly what the OS sends while wiring up printer registration.
    /// </summary>
    public event EventHandler<string>? RequestLog;

    public IppPrinterOptions Options => _options;

    public void Start()
    {
        if (_acceptLoop is not null) return;
        _cts = new CancellationTokenSource();
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopping */ }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }
        _acceptLoop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break; // listener stopped
            }

            _ = Task.Run(() => HandleRequestAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            byte[] body;
            using (var ms = new MemoryStream())
            {
                await context.Request.InputStream.CopyToAsync(ms).ConfigureAwait(false);
                body = ms.ToArray();
            }

            string method = context.Request.HttpMethod;
            string rawUrl = context.Request.RawUrl ?? "/";

            IppMessage response;
            string outcome;
            try
            {
                var request = IppReader.Parse(body);
                outcome = $"IPP {request.Operation} (v{request.VersionMajor}.{request.VersionMinor}, req#{request.RequestId})";
                response = Dispatch(request);
                outcome += $" -> {response.Status}";
            }
            catch (Exception ex)
            {
                outcome = $"not IPP / unparseable ({ex.GetType().Name})";
                response = NewResponse(0, IppStatus.ClientErrorBadRequest);
            }

            RequestLog?.Invoke(this, $"{method} {rawUrl} [{body.Length} bytes] {outcome}");

            byte[] responseBytes = IppWriter.Serialize(response);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/ipp";
            context.Response.ContentLength64 = responseBytes.Length;
            await context.Response.OutputStream.WriteAsync(responseBytes).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: nothing more we can do for a broken connection.
        }
        finally
        {
            try { context.Response.OutputStream.Close(); } catch { /* ignore */ }
        }
    }

    private IppMessage Dispatch(IppMessage request)
    {
        return request.Operation switch
        {
            IppOperation.GetPrinterAttributes => GetPrinterAttributes(request),
            IppOperation.ValidateJob => SimpleOk(request),
            IppOperation.PrintJob => PrintJob(request),
            IppOperation.CreateJob => CreateJob(request),
            IppOperation.SendDocument => SendDocument(request),
            IppOperation.GetJobAttributes => SimpleOk(request),
            IppOperation.GetJobs => SimpleOk(request),
            _ => NewResponse(request.RequestId, IppStatus.ServerErrorOperationNotSupported),
        };
    }

    private IppMessage GetPrinterAttributes(IppMessage request)
    {
        var response = NewResponse(request.RequestId, IppStatus.SuccessfulOk);
        var p = response.AddGroup(IppTag.PrinterAttributes);

        int upTime = (int)Math.Max(1, (DateTimeOffset.UtcNow - _startedAt).TotalSeconds);

        // --- Identity / URIs ---
        p.Add(new IppAttribute("printer-uri-supported", IppTag.Uri, _options.PrinterUri));
        p.Add(new IppAttribute("uri-authentication-supported", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("uri-security-supported", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("printer-name", IppTag.NameWithoutLanguage, _options.PrinterName));
        p.Add(new IppAttribute("printer-info", IppTag.TextWithoutLanguage, _options.PrinterName));
        p.Add(new IppAttribute("printer-location", IppTag.TextWithoutLanguage, ""));
        p.Add(new IppAttribute("printer-make-and-model", IppTag.TextWithoutLanguage, "OpenLeanPrint Virtual Printer"));
        p.Add(new IppAttribute("printer-uuid", IppTag.Uri, PrinterUuid));
        p.Add(new IppAttribute("printer-device-id", IppTag.TextWithoutLanguage,
            "MFG:OpenLeanPrint;MDL:Virtual Printer;CMD:PDF;CLS:PRINTER;"));

        // --- State ---
        p.Add(new IppAttribute("printer-state", IppTag.Enum, (int)IppPrinterState.Idle));
        p.Add(new IppAttribute("printer-state-reasons", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("printer-is-accepting-jobs", IppTag.Boolean, true));
        p.Add(new IppAttribute("queued-job-count", IppTag.Integer, _pendingJobs.Count));
        p.Add(new IppAttribute("printer-up-time", IppTag.Integer, upTime));
        p.Add(new IppAttribute("pages-per-minute", IppTag.Integer, 20));

        // --- Protocol / operations ---
        p.Add(new IppAttribute("ipp-versions-supported", IppTag.Keyword, "1.1", "2.0"));
        p.Add(new IppAttribute("ipp-features-supported", IppTag.Keyword, "ipp-everywhere"));
        p.Add(new IppAttribute("operations-supported", IppTag.Enum,
            (int)IppOperation.PrintJob, (int)IppOperation.ValidateJob,
            (int)IppOperation.CreateJob, (int)IppOperation.SendDocument,
            (int)IppOperation.CancelJob, (int)IppOperation.GetJobAttributes,
            (int)IppOperation.GetJobs, (int)IppOperation.GetPrinterAttributes));
        p.Add(new IppAttribute("charset-configured", IppTag.Charset, "utf-8"));
        p.Add(new IppAttribute("charset-supported", IppTag.Charset, "utf-8"));
        p.Add(new IppAttribute("natural-language-configured", IppTag.NaturalLanguage, "en"));
        p.Add(new IppAttribute("generated-natural-language-supported", IppTag.NaturalLanguage, "en"));
        p.Add(new IppAttribute("pdl-override-supported", IppTag.Keyword, "attempted"));
        p.Add(new IppAttribute("compression-supported", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("job-creation-attributes-supported", IppTag.Keyword,
            "copies", "media", "sides", "print-color-mode", "print-quality",
            "printer-resolution", "orientation-requested"));

        // --- Document formats (PDF preferred; raster advertised for IPP Everywhere acceptance) ---
        p.Add(new IppAttribute("document-format-default", IppTag.MimeMediaType, "application/pdf"));
        p.Add(new IppAttribute("document-format-supported", IppTag.MimeMediaType,
            "application/pdf", "image/pwg-raster", "application/octet-stream"));

        // --- Copies / finishings / quality ---
        p.Add(new IppAttribute("copies-default", IppTag.Integer, 1));
        p.Add(new IppAttribute("copies-supported", IppTag.RangeOfInteger, IppValues.RangeOfInteger(1, 999)));
        p.Add(new IppAttribute("finishings-default", IppTag.Enum, 3)); // none
        p.Add(new IppAttribute("finishings-supported", IppTag.Enum, 3));
        p.Add(new IppAttribute("print-quality-default", IppTag.Enum, 4)); // normal
        p.Add(new IppAttribute("print-quality-supported", IppTag.Enum, 3, 4, 5));
        p.Add(new IppAttribute("orientation-requested-default", IppTag.Enum, 3)); // portrait
        p.Add(new IppAttribute("orientation-requested-supported", IppTag.Enum, 3, 4)); // portrait, landscape
        // Colour, because this printer does not print anything: it hands the
        // document on to a real one. Advertising monochrome made Windows convert
        // every job to greyscale on the way in, and a colour document arrived
        // already grey - the loss happens before the file reaches us and cannot
        // be undone afterwards.
        p.Add(new IppAttribute("color-supported", IppTag.Boolean, true));
        p.Add(new IppAttribute("print-color-mode-default", IppTag.Keyword, "color"));
        p.Add(new IppAttribute("print-color-mode-supported", IppTag.Keyword, "color", "monochrome"));

        // --- Sides ---
        p.Add(new IppAttribute("sides-default", IppTag.Keyword, "one-sided"));
        p.Add(new IppAttribute("sides-supported", IppTag.Keyword,
            "one-sided", "two-sided-long-edge", "two-sided-short-edge"));

        // --- Resolution ---
        p.Add(new IppAttribute("printer-resolution-default", IppTag.Resolution, IppValues.Resolution(300, 300)));
        p.Add(new IppAttribute("printer-resolution-supported", IppTag.Resolution, IppValues.Resolution(300, 300)));

        // --- Media ---
        p.Add(new IppAttribute("media-default", IppTag.Keyword, "iso_a4_210x297mm"));
        p.Add(new IppAttribute("media-ready", IppTag.Keyword, "iso_a4_210x297mm"));
        p.Add(new IppAttribute("media-supported", IppTag.Keyword,
            "iso_a4_210x297mm", "na_letter_8.5x11in"));
        p.Add(new IppAttribute("media-source-supported", IppTag.Keyword, "auto"));
        p.Add(new IppAttribute("media-type-supported", IppTag.Keyword, "stationery"));
        p.Add(new IppAttribute("media-left-margin-supported", IppTag.Integer, 0));
        p.Add(new IppAttribute("media-right-margin-supported", IppTag.Integer, 0));
        p.Add(new IppAttribute("media-top-margin-supported", IppTag.Integer, 0));
        p.Add(new IppAttribute("media-bottom-margin-supported", IppTag.Integer, 0));
        p.Add(new IppAttribute("output-bin-default", IppTag.Keyword, "face-down"));
        p.Add(new IppAttribute("output-bin-supported", IppTag.Keyword, "face-down"));

        // --- PWG Raster / URF descriptors (required for the class driver to accept a driverless queue) ---
        p.Add(new IppAttribute("pwg-raster-document-resolution-supported", IppTag.Resolution, IppValues.Resolution(300, 300)));
        p.Add(new IppAttribute("pwg-raster-document-sheet-back", IppTag.Keyword, "normal"));
        p.Add(new IppAttribute("pwg-raster-document-type-supported", IppTag.Keyword, "sgray_8", "srgb_8"));
        p.Add(new IppAttribute("urf-supported", IppTag.Keyword,
            "CP1", "IS1", "MT1-2-3-4-5-6", "OB10", "PQ4", "RS300", "SRGB24", "V1.4", "W8", "DM1"));

        return response;
    }

    private IppMessage PrintJob(IppMessage request)
    {
        int jobId = Interlocked.Increment(ref _nextJobId);
        FinalizeJob(jobId, request, request.Data);
        return JobResponse(request, jobId, IppJobState.Completed, "job-completed-successfully");
    }

    private IppMessage CreateJob(IppMessage request)
    {
        int jobId = Interlocked.Increment(ref _nextJobId);
        _pendingJobs[jobId] = new PendingJob
        {
            JobId = jobId,
            JobName = request.FindAttribute("job-name")?.AsString(),
            UserName = request.FindAttribute("requesting-user-name")?.AsString(),
            Sides = request.FindAttribute("sides")?.AsString(),
            ColorMode = request.FindAttribute("print-color-mode")?.AsString(),
        };
        return JobResponse(request, jobId, IppJobState.Pending, "none");
    }

    private IppMessage SendDocument(IppMessage request)
    {
        int jobId = request.FindAttribute("job-id")?.AsInt() ?? 0;
        if (jobId == 0 || !_pendingJobs.TryGetValue(jobId, out var pending))
            return NewResponse(request.RequestId, IppStatus.ClientErrorNotFound);

        if (request.Data.Length > 0)
            pending.Buffer.Write(request.Data, 0, request.Data.Length);

        bool lastDocument = request.FindAttribute("last-document") is { } a
                            && a.Values.Count > 0 && a.Values[0] is true;

        if (!lastDocument)
            return JobResponse(request, jobId, IppJobState.Pending, "none");

        _pendingJobs.TryRemove(jobId, out _);
        FinalizeJob(jobId, request, pending.Buffer.ToArray(), pending.JobName, pending.UserName,
                    pending.Sides, pending.ColorMode);
        return JobResponse(request, jobId, IppJobState.Completed, "job-completed-successfully");
    }

    private CapturedJob FinalizeJob(int jobId, IppMessage request, byte[] data,
                                    string? jobName = null, string? userName = null,
                                    string? sides = null, string? colorMode = null)
    {
        string format = request.FindAttribute("document-format")?.AsString() ?? "application/pdf";
        var captured = new CapturedJob
        {
            JobId = jobId,
            JobName = jobName ?? request.FindAttribute("job-name")?.AsString(),
            UserName = userName ?? request.FindAttribute("requesting-user-name")?.AsString(),
            DocumentFormat = format,
            Sides = sides ?? request.FindAttribute("sides")?.AsString(),
            ColorMode = colorMode ?? request.FindAttribute("print-color-mode")?.AsString(),
            Data = data,
        };

        if (captured.IsPdf && data.Length > 0)
        {
            try
            {
                captured.Document = PdfPageExtractor.ToPrintDocument(
                    data, captured.JobName ?? _options.PrinterName, captured.UserName);
            }
            catch (Exception ex)
            {
                captured.ParseError = ex.Message;
            }
        }

        JobCaptured?.Invoke(this, captured);
        return captured;
    }

    private IppMessage JobResponse(IppMessage request, int jobId, IppJobState state, string reason)
    {
        var response = NewResponse(request.RequestId, IppStatus.SuccessfulOk);
        var job = response.AddGroup(IppTag.JobAttributes);
        job.Add(new IppAttribute("job-uri", IppTag.Uri, $"{_options.PrinterUri}/jobs/{jobId}"));
        job.Add(new IppAttribute("job-id", IppTag.Integer, jobId));
        job.Add(new IppAttribute("job-state", IppTag.Enum, (int)state));
        job.Add(new IppAttribute("job-state-reasons", IppTag.Keyword, reason));
        return response;
    }

    private IppMessage SimpleOk(IppMessage request) =>
        NewResponse(request.RequestId, IppStatus.SuccessfulOk);

    private static IppMessage NewResponse(int requestId, IppStatus status)
    {
        var response = new IppMessage
        {
            VersionMajor = 1,
            VersionMinor = 1,
            OperationOrStatus = (short)status,
            RequestId = requestId == 0 ? 1 : requestId,
        };
        var op = response.AddGroup(IppTag.OperationAttributes);
        op.Add(new IppAttribute("attributes-charset", IppTag.Charset, "utf-8"));
        op.Add(new IppAttribute("attributes-natural-language", IppTag.NaturalLanguage, "en"));
        return response;
    }

    public void Dispose()
    {
        try { StopAsync().GetAwaiter().GetResult(); } catch { /* ignore */ }
        _cts?.Dispose();
        (_listener as IDisposable).Dispose();
    }

    /// <summary>
    /// A job between Create-Job and its last Send-Document.
    /// <para>
    /// Everything the client asked for arrives with Create-Job; Send-Document
    /// carries the bytes and little else. So what is wanted has to be kept here
    /// until the document is complete - reading it off the Send-Document request
    /// finds nothing, which is exactly how a measurement of two-sided printing
    /// came back empty and looked like the print path had asked for nothing.
    /// </para>
    /// </summary>
    private sealed class PendingJob
    {
        public required int JobId { get; init; }
        public string? JobName { get; init; }
        public string? UserName { get; init; }
        public string? Sides { get; init; }
        public string? ColorMode { get; init; }
        public MemoryStream Buffer { get; } = new();
    }
}
