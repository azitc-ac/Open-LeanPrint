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

            IppMessage response;
            try
            {
                var request = IppReader.Parse(body);
                response = Dispatch(request);
            }
            catch (Exception)
            {
                response = NewResponse(0, IppStatus.ClientErrorBadRequest);
            }

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

        p.Add(new IppAttribute("printer-uri-supported", IppTag.Uri, _options.PrinterUri));
        p.Add(new IppAttribute("uri-authentication-supported", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("uri-security-supported", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("printer-name", IppTag.NameWithoutLanguage, _options.PrinterName));
        p.Add(new IppAttribute("printer-info", IppTag.TextWithoutLanguage, _options.PrinterName));
        p.Add(new IppAttribute("printer-make-and-model", IppTag.TextWithoutLanguage, "OpenLeanPrint Virtual Printer"));
        p.Add(new IppAttribute("printer-state", IppTag.Enum, (int)IppPrinterState.Idle));
        p.Add(new IppAttribute("printer-state-reasons", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("ipp-versions-supported", IppTag.Keyword, "1.1", "2.0"));
        p.Add(new IppAttribute("operations-supported", IppTag.Enum,
            (int)IppOperation.PrintJob, (int)IppOperation.ValidateJob,
            (int)IppOperation.CreateJob, (int)IppOperation.SendDocument,
            (int)IppOperation.GetJobAttributes, (int)IppOperation.GetJobs,
            (int)IppOperation.GetPrinterAttributes));
        p.Add(new IppAttribute("charset-configured", IppTag.Charset, "utf-8"));
        p.Add(new IppAttribute("charset-supported", IppTag.Charset, "utf-8"));
        p.Add(new IppAttribute("natural-language-configured", IppTag.NaturalLanguage, "en"));
        p.Add(new IppAttribute("generated-natural-language-supported", IppTag.NaturalLanguage, "en"));
        p.Add(new IppAttribute("document-format-default", IppTag.MimeMediaType, "application/pdf"));
        p.Add(new IppAttribute("document-format-supported", IppTag.MimeMediaType,
            "application/pdf", "application/octet-stream"));
        p.Add(new IppAttribute("printer-is-accepting-jobs", IppTag.Boolean, true));
        p.Add(new IppAttribute("queued-job-count", IppTag.Integer, _pendingJobs.Count));
        p.Add(new IppAttribute("pdl-override-supported", IppTag.Keyword, "attempted"));
        p.Add(new IppAttribute("printer-up-time", IppTag.Integer, upTime));
        p.Add(new IppAttribute("compression-supported", IppTag.Keyword, "none"));
        p.Add(new IppAttribute("media-default", IppTag.Keyword, "iso_a4_210x297mm"));
        p.Add(new IppAttribute("media-supported", IppTag.Keyword,
            "iso_a4_210x297mm", "na_letter_8.5x11in"));
        p.Add(new IppAttribute("sides-default", IppTag.Keyword, "one-sided"));
        p.Add(new IppAttribute("sides-supported", IppTag.Keyword,
            "one-sided", "two-sided-long-edge", "two-sided-short-edge"));
        p.Add(new IppAttribute("printer-resolution-default", IppTag.Resolution, IppValues.Resolution(300, 300)));
        p.Add(new IppAttribute("printer-resolution-supported", IppTag.Resolution, IppValues.Resolution(300, 300)));

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
        FinalizeJob(jobId, request, pending.Buffer.ToArray(), pending.JobName, pending.UserName);
        return JobResponse(request, jobId, IppJobState.Completed, "job-completed-successfully");
    }

    private CapturedJob FinalizeJob(int jobId, IppMessage request, byte[] data,
                                    string? jobName = null, string? userName = null)
    {
        string format = request.FindAttribute("document-format")?.AsString() ?? "application/pdf";
        var captured = new CapturedJob
        {
            JobId = jobId,
            JobName = jobName ?? request.FindAttribute("job-name")?.AsString(),
            UserName = userName ?? request.FindAttribute("requesting-user-name")?.AsString(),
            DocumentFormat = format,
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

    private sealed class PendingJob
    {
        public required int JobId { get; init; }
        public string? JobName { get; init; }
        public string? UserName { get; init; }
        public MemoryStream Buffer { get; } = new();
    }
}
