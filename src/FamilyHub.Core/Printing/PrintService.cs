using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FamilyHub.Core.Printing;

/// <summary>
/// Printing for every menu: send a PDF to the family's print server and follow it until it is printed.
/// Jobs sent from Family Hub are followed by <see cref="PrintJobWorker"/>; <see cref="JobChanged"/> tells every screen
/// when one moves on ("Udskriver" → "Udskrevet") or needs attention ("Printeren mangler papir."). See docs/printer.md.
/// </summary>
public sealed class PrintService(PrintApiClient api, IOptionsMonitor<PrintApiOptions> options, TimeProvider time, ILogger<PrintService> logger)
{
    /// <summary>How often unfinished jobs are checked. The print server itself checks the printer every 5 s.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    /// <summary>A job still unfinished after this long is no longer followed (the print server retries for ~15 min).</summary>
    public static readonly TimeSpan FollowFor = TimeSpan.FromHours(1);

    private readonly Lock gate = new();
    private readonly Dictionary<Guid, (PrintJob Job, DateTimeOffset Since)> active = [];
    private readonly SemaphoreSlim jobAdded = new(0);

    /// <summary>Address and key are set. False = show an InfoBox instead of a print button.</summary>
    public bool IsConfigured => api.IsConfigured;

    /// <summary>Raised when a job sent from Family Hub is sent, moves on or finishes. May be raised from any thread.</summary>
    public event Action<PrintJob>? JobChanged;

    /// <summary>Unfinished jobs sent from Family Hub, oldest first.</summary>
    public IReadOnlyList<PrintJob> ActiveJobs
    {
        get
        {
            lock (gate)
            {
                return [.. active.Values.Select(a => a.Job).OrderBy(j => j.CreatedAt)];
            }
        }
    }

    internal bool HasActiveJobs
    {
        get
        {
            lock (gate)
            {
                return active.Count > 0;
            }
        }
    }

    /// <summary>The print server's printers and whether they answer right now (takes up to ~3 s).</summary>
    public Task<PrintServerStatus> GetStatusAsync(CancellationToken cancellationToken = default) => api.GetStatusAsync(cancellationToken);

    /// <summary>
    /// Sends a PDF to the printer and returns the job at once (usually <see cref="PrintJobStatus.Queued"/>).
    /// The document and options are checked first (<see cref="PrintApiError.Invalid"/> with a Danish message).
    /// Throws <see cref="PrintApiException"/>.
    /// </summary>
    public async Task<PrintJob> PrintPdfAsync(ReadOnlyMemory<byte> pdf, PrintOptions? printOptions = null, CancellationToken cancellationToken = default)
    {
        printOptions ??= PrintOptions.Default;
        if (!IsConfigured)
        {
            throw PrintApiException.NotConfigured();
        }

        if (PrintValidation.Validate(pdf.Span, printOptions) is { } problem)
        {
            throw PrintApiException.Invalid(problem);
        }

        var printerId = printOptions.PrinterId ?? options.CurrentValue.PrinterId ?? await FindOnlyPrinterAsync(cancellationToken);
        var job = await api.SubmitAsync(printerId, pdf, printOptions, cancellationToken);
        logger.LogInformation("Print job {JobId} sent to printer {PrinterId}: {Pages} page(s) × {Copies}", job.Id, printerId, job.PagesToPrint, job.Copies);

        Follow(job);
        return job;
    }

    /// <summary>As <see cref="PrintPdfAsync(ReadOnlyMemory{byte}, PrintOptions?, CancellationToken)"/>, reading the PDF from a stream.</summary>
    public async Task<PrintJob> PrintPdfAsync(Stream pdf, PrintOptions? printOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (pdf.CanSeek && pdf.Length - pdf.Position > PrintValidation.MaxDocumentBytes)
        {
            throw PrintApiException.Invalid("Dokumentet er for stort til printeren (højst 75 MB).");
        }

        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, cancellationToken);
        return await PrintPdfAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), printOptions, cancellationToken);
    }

    /// <summary>The job's status now. Null if the print server no longer has it.</summary>
    public async Task<PrintJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await api.GetJobAsync(id, cancellationToken);
        if (job is null)
        {
            Forget(id);
        }
        else
        {
            Update(job);
        }

        return job;
    }

    /// <summary>Jobs sent with Family Hub's key (also from other screens and earlier days), newest first.</summary>
    public Task<PrintJobPage> GetJobsAsync(PrintJobQuery? query = null, CancellationToken cancellationToken = default) =>
        api.GetJobsAsync(query ?? new PrintJobQuery(), cancellationToken);

    /// <summary>
    /// Cancels a job: removed from the queue, or stopped on the printer (pages already printed stay printed).
    /// <see cref="PrintApiError.Conflict"/> if it is being sent right now or is already done.
    /// </summary>
    public async Task<PrintJob> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await api.CancelAsync(id, cancellationToken);
        Update(job);
        return job;
    }

    /// <summary>Checks every followed job once. Called by <see cref="PrintJobWorker"/>. Returns whether any are still unfinished.</summary>
    internal async Task<bool> RefreshActiveJobsAsync(CancellationToken cancellationToken)
    {
        List<(PrintJob Job, DateTimeOffset Since)> followed;
        lock (gate)
        {
            followed = [.. active.Values];
        }

        foreach (var (job, since) in followed)
        {
            if (time.GetUtcNow() - since > FollowFor)
            {
                logger.LogWarning("Print job {JobId} is still {Status} after {Hours} h – no longer followed", job.Id, job.Status, FollowFor.TotalHours);
                Forget(job.Id);
                continue;
            }

            PrintJob? fresh;
            try
            {
                fresh = await api.GetJobAsync(job.Id, cancellationToken);
            }
            catch (PrintApiException ex)
            {
                // The print server is probably down or restarting – try again next round.
                logger.LogDebug(ex, "Could not check print job {JobId}", job.Id);
                break;
            }

            if (fresh is null)
            {
                Forget(job.Id);
            }
            else
            {
                Update(fresh);
            }
        }

        return HasActiveJobs;
    }

    /// <summary>Waits until a job is sent. Called by <see cref="PrintJobWorker"/> when nothing is followed.</summary>
    internal async Task WaitForJobsAsync(CancellationToken cancellationToken)
    {
        await jobAdded.WaitAsync(cancellationToken);
        while (jobAdded.Wait(0))
        {
            // One wake-up is enough, however many jobs were sent.
        }
    }

    private async Task<int> FindOnlyPrinterAsync(CancellationToken cancellationToken)
    {
        var status = await api.GetStatusAsync(cancellationToken);
        return status.Printers switch
        {
            [var only] => only.Id,
            [] => throw new PrintApiException(PrintApiError.NoPrinter, "Printserveren har ingen printer."),
            _ => throw new PrintApiException(PrintApiError.NoPrinter, "Der er flere printere. Vælg, hvilken Family Hub skal bruge."),
        };
    }

    private void Follow(PrintJob job)
    {
        if (!job.IsFinished)
        {
            lock (gate)
            {
                active[job.Id] = (job, time.GetUtcNow());
            }

            jobAdded.Release();
        }

        Notify(job);
    }

    /// <summary>Stores a newer version of a followed job and tells the screens if anything they show has changed.</summary>
    private void Update(PrintJob job)
    {
        lock (gate)
        {
            if (!active.TryGetValue(job.Id, out var current))
            {
                return;
            }

            if (job.IsFinished)
            {
                active.Remove(job.Id);
            }
            else
            {
                active[job.Id] = (job, current.Since);
            }

            if (job.IsFinished == current.Job.IsFinished && SameProgress(job, current.Job))
            {
                return;
            }
        }

        if (job.IsFinished)
        {
            logger.LogInformation("Print job {JobId} finished: {Status} {Message}", job.Id, job.Status, job.StatusMessage);
        }

        Notify(job);
    }

    private void Forget(Guid id)
    {
        lock (gate)
        {
            active.Remove(id);
        }
    }

    // UpdatedAt changes on every check by the print server, so only what a screen shows counts.
    private static bool SameProgress(PrintJob a, PrintJob b) =>
        a.Status == b.Status
        && a.Problem == b.Problem
        && a.ImpressionsCompleted == b.ImpressionsCompleted
        && a.Attempts == b.Attempts;

    private void Notify(PrintJob job)
    {
        if (JobChanged is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<PrintJob>>())
        {
            try
            {
                handler(job);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A PrintService.JobChanged handler failed");
            }
        }
    }
}
