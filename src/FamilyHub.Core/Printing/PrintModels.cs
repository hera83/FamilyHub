namespace FamilyHub.Core.Printing;

/// <summary>A print job's life: Queued → Processing → Printing → Completed / Failed / Canceled.</summary>
public enum PrintJobStatus
{
    /// <summary>Waiting in the print server's queue (also while waiting to try an unreachable printer again).</summary>
    Queued,

    /// <summary>Being converted and sent to the printer.</summary>
    Processing,

    /// <summary>The printer has it and is printing.</summary>
    Printing,

    Completed,
    Failed,
    Canceled,
}

public static class PrintJobStatusExtensions
{
    /// <summary>The Danish word for the status – for a badge or a line of text.</summary>
    public static string Label(this PrintJobStatus status) => status switch
    {
        PrintJobStatus.Queued => "I kø",
        PrintJobStatus.Processing => "Sendes til printeren",
        PrintJobStatus.Printing => "Udskriver",
        PrintJobStatus.Completed => "Udskrevet",
        PrintJobStatus.Failed => "Mislykkedes",
        PrintJobStatus.Canceled => "Annulleret",
        _ => status.ToString(),
    };

    public static bool IsFinished(this PrintJobStatus status) =>
        status is PrintJobStatus.Completed or PrintJobStatus.Failed or PrintJobStatus.Canceled;
}

/// <summary>How to print a document. Everything is optional; the defaults suit a shopping list.</summary>
public sealed record PrintOptions
{
    public const int MaxCopies = 99;
    public const int MaxPagesLength = 200;

    public static PrintOptions Default { get; } = new();

    /// <summary>The printer. Null = <see cref="PrintApiOptions.PrinterId"/>, or the only printer on the print server.</summary>
    public int? PrinterId { get; init; }

    /// <summary>1–99.</summary>
    public int Copies { get; init; } = 1;

    /// <summary>False = black and white (saves the colour ink).</summary>
    public bool Color { get; init; }

    /// <summary>Both sides of the paper, flipped on the long edge like a book.</summary>
    public bool Duplex { get; init; }

    /// <summary>Which pages, e.g. "2-9", "5", "1,3,5-7" or "3-" (page 3 to the end). Null = all.</summary>
    public string? Pages { get; init; }
}

/// <summary>A print job as the print server reports it. Times are UTC.</summary>
public sealed record PrintJob
{
    public Guid Id { get; init; }

    public int PrinterId { get; init; }

    public string PrinterName { get; init; } = "";

    public PrintJobStatus Status { get; init; }

    /// <summary>True once the job can no longer change (Completed, Failed or Canceled).</summary>
    public bool IsFinished { get; init; }

    /// <summary>The print server's own explanation – English, for the log. Show <see cref="Problem"/> instead.</summary>
    public string? StatusMessage { get; init; }

    /// <summary>Pages in the PDF.</summary>
    public int PageCount { get; init; }

    /// <summary>Pages printed per copy.</summary>
    public int PagesToPrint { get; init; }

    /// <summary>The chosen pages ("2-9"), or null for all.</summary>
    public string? Pages { get; init; }

    public int Copies { get; init; } = 1;

    public bool Color { get; init; }

    public bool Duplex { get; init; }

    /// <summary>How many times the print server has tried to reach the printer.</summary>
    public int Attempts { get; init; }

    public DateTimeOffset? NextAttemptAt { get; init; }

    /// <summary>The printer's own job state (IPP), e.g. "processing" or "processing-stopped".</summary>
    public string? PrinterJobState { get; init; }

    public IReadOnlyList<string> PrinterJobStateReasons { get; init; } = [];

    /// <summary>Sides printed so far, if the printer tells.</summary>
    public int? ImpressionsCompleted { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>The status in Danish ("Udskriver").</summary>
    public string StatusText => Status.Label();

    /// <summary>What needs attention, in Danish ("Printeren mangler papir."), or null when all is well.</summary>
    public string? Problem => PrintTexts.JobProblem(this);
}

/// <summary>One page of the print server's job list, newest first.</summary>
public sealed record PrintJobPage
{
    public IReadOnlyList<PrintJob> Items { get; init; } = [];

    public int TotalCount { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; }

    public bool HasMore => Page * PageSize < TotalCount;
}

/// <summary>Filters for <c>GET /Print/GetAll</c>. The print server only shows jobs sent with Family Hub's own key.</summary>
public sealed record PrintJobQuery
{
    public const int MaxPageSize = 500;

    public int? PrinterId { get; init; }

    public PrintJobStatus? Status { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;
}

/// <summary>A printer on the print server, as the health check saw it just now.</summary>
public sealed record PrinterInfo
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    /// <summary>The printer answered within a few seconds.</summary>
    public bool IsOnline { get; init; }

    /// <summary>"idle", "processing" or "stopped". Null when offline.</summary>
    public string? State { get; init; }

    /// <summary>IPP keywords, e.g. "media-empty-error", "marker-supply-low-warning".</summary>
    public IReadOnlyList<string> StateReasons { get; init; } = [];

    public bool? IsAcceptingJobs { get; init; }

    /// <summary>Why it counts as offline – English, for the log.</summary>
    public string? Error { get; init; }

    /// <summary>Online, not stopped, and accepting jobs. A job sent to a printer that cannot print waits in the queue.</summary>
    public bool IsReady => IsOnline && IsAcceptingJobs != false && !string.Equals(State, "stopped", StringComparison.OrdinalIgnoreCase);

    /// <summary>What needs attention, in Danish ("Printeren mangler papir."), or null when all is well.</summary>
    public string? Problem => PrintTexts.PrinterProblem(this);
}

/// <summary>The print server's health check: every active printer, online or not.</summary>
public sealed record PrintServerStatus
{
    /// <summary>At least one printer is online.</summary>
    public bool IsHealthy { get; init; }

    public IReadOnlyList<PrinterInfo> Printers { get; init; } = [];

    public DateTimeOffset CheckedAt { get; init; }

    public PrinterInfo? Find(int id) => Printers.FirstOrDefault(p => p.Id == id);
}
