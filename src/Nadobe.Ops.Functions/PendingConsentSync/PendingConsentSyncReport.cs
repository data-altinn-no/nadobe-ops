using Nadobe.Ops.Functions.AppInsights;

namespace Nadobe.Ops.Functions.PendingConsentSync;

/// <summary>One completed run of the pending consent sync in Core, as read from its summary log line.</summary>
/// <param name="CompletedAt">When the summary line was logged.</param>
/// <param name="NotChecked">Above zero only when the run was stopped after too many failures in a row.</param>
/// <param name="Duration">Null when the timer line of the run was not found.</param>
public sealed record PendingConsentSyncRun(
    DateTimeOffset CompletedAt,
    int Checked,
    int Granted,
    int Denied,
    int Failed,
    int NotChecked,
    TimeSpan? Duration)
{
    public bool WasStopped => NotChecked > 0;

    public bool HasProblems => Failed > 0 || WasStopped;
}

/// <param name="Runs">The runs that completed in the window, oldest first.</param>
/// <param name="Failure">Why the logs could not be read; null when they could.</param>
public sealed record PendingConsentSyncSourceReport(
    LogSource Source,
    IReadOnlyList<PendingConsentSyncRun> Runs,
    string? Failure = null)
{
    /// <summary>The logs were readable and hold no completed run: the timer did not fire, or the run did not finish.</summary>
    public bool IsMissing => Failure is null && Runs.Count == 0;

    public bool HasProblems => Failure is not null || IsMissing || Runs.Any(run => run.HasProblems);
}

/// <param name="Sources">One entry per configured log source, in configured order.</param>
/// <param name="From">Start of the window the logs were read for.</param>
/// <param name="To">End of the window, which is when the report was made.</param>
public sealed record PendingConsentSyncReport(
    IReadOnlyList<PendingConsentSyncSourceReport> Sources,
    DateTimeOffset From,
    DateTimeOffset To)
{
    public bool HasProblems => Sources.Any(source => source.HasProblems);

    /// <summary>The sources whose logs could not be read.</summary>
    public IReadOnlyList<PendingConsentSyncSourceReport> Unreadable =>
        [.. Sources.Where(source => source.Failure is not null)];
}
