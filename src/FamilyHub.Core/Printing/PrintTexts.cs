namespace FamilyHub.Core.Printing;

/// <summary>
/// Danish texts for what the printer and the print server report (IPP keywords, job states).
/// The print server itself speaks English – screens show these instead. Pure functions – unit-tested.
/// </summary>
public static class PrintTexts
{
    public const string PrinterStopped = "Printeren er stoppet. Se printerens skærm.";

    // Most urgent first: the first reason that matches decides the text.
    private static readonly (string Keyword, string Text)[] Reasons =
    [
        ("media-jam", "Der sidder papir fast i printeren."),
        ("media-empty", "Printeren mangler papir."),
        ("media-needed", "Printeren mangler papir."),
        ("input-tray-missing", "Papirbakken mangler."),
        ("door-open", "Et låg på printeren står åbent."),
        ("cover-open", "Et låg på printeren står åbent."),
        ("interlock-open", "Et låg på printeren står åbent."),
        ("marker-supply-empty", "Blækket er brugt op."),
        ("toner-empty", "Blækket er brugt op."),
        ("marker-supply-missing", "Der mangler en blækpatron."),
        ("shutdown", "Printeren er slukket."),
        ("offline", "Printeren er slukket."),
        ("paused", "Printeren er sat på pause."),
        ("printer-stopped", PrinterStopped),
        ("marker-supply-low", "Blækket er ved at slippe op."),
        ("toner-low", "Blækket er ved at slippe op."),
    ];

    /// <summary>
    /// The most important of the printer's IPP reasons in Danish ("media-empty-error" → "Printeren mangler papir."),
    /// or null if none of them needs attention. "-report" reasons are only information and are ignored.
    /// </summary>
    public static string? DescribeReasons(IEnumerable<string>? reasons)
    {
        var keywords = (reasons ?? [])
            .Select(r => r.Trim().ToLowerInvariant())
            .Where(r => r.Length > 0 && r != "none" && !r.EndsWith("-report", StringComparison.Ordinal))
            .Select(r => r.EndsWith("-error", StringComparison.Ordinal) ? r[..^"-error".Length]
                : r.EndsWith("-warning", StringComparison.Ordinal) ? r[..^"-warning".Length]
                : r)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (keyword, text) in Reasons)
        {
            if (keywords.Contains(keyword))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>What needs attention on a printer, or null when it is ready.</summary>
    public static string? PrinterProblem(PrinterInfo printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (!printer.IsOnline)
        {
            return "Printeren svarer ikke. Er den tændt?";
        }

        var reason = DescribeReasons(printer.StateReasons);
        if (reason is not null)
        {
            return reason;
        }

        if (string.Equals(printer.State, "stopped", StringComparison.OrdinalIgnoreCase))
        {
            return PrinterStopped;
        }

        return printer.IsAcceptingJobs == false ? "Printeren tager ikke imod udskrifter lige nu." : null;
    }

    /// <summary>What needs attention for a print job, or null while it runs normally (and when it is done).</summary>
    public static string? JobProblem(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.Status switch
        {
            PrintJobStatus.Failed => "Udskriften mislykkedes.",
            PrintJobStatus.Queued when job.Attempts > 0 => "Printeren svarer ikke. Prøver igen om lidt.",
            PrintJobStatus.Printing when string.Equals(job.PrinterJobState, "processing-stopped", StringComparison.OrdinalIgnoreCase) =>
                DescribeReasons(job.PrinterJobStateReasons) ?? PrinterStopped,
            _ => null,
        };
    }
}
