using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nadobe.Ops.Functions.PendingConsentSync;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions;

/// <summary>
/// Reports each run of the pending consent sync in Core to Slack, read from the logs of Core.
/// </summary>
public class PendingConsentSyncReportFunction(
    PendingConsentSyncReporter reporter,
    ISlackNotifier notifier,
    ILogger<PendingConsentSyncReportFunction> logger)
{
    /// <summary>
    /// Half an hour after each run of the sync, which Core starts Monday to Friday at 07, 11, 15 and
    /// 19 UTC. The half hour leaves time for the run to finish and for its log lines to arrive.
    /// </summary>
    private const string WorkdaysHalfPastTheSyncHours = "0 30 7,11,15,19 * * 1-5";

    /// <summary>Half an hour after the weekend run of the sync, which Core starts at 11 UTC.</summary>
    private const string WeekendsHalfPastTheSyncHour = "0 30 11 * * 0,6";

    [Function(nameof(ReportPendingConsentSyncOnWorkdays))]
    public Task ReportPendingConsentSyncOnWorkdays(
        [TimerTrigger(WorkdaysHalfPastTheSyncHours)] TimerInfo timer,
        CancellationToken cancellationToken) =>
        ReportAsync(cancellationToken);

    /// <summary>A timer has one schedule, hence a second timer for the weekend.</summary>
    [Function(nameof(ReportPendingConsentSyncOnWeekends))]
    public Task ReportPendingConsentSyncOnWeekends(
        [TimerTrigger(WeekendsHalfPastTheSyncHour)] TimerInfo timer,
        CancellationToken cancellationToken) =>
        ReportAsync(cancellationToken);

    private async Task ReportAsync(CancellationToken cancellationToken)
    {
        var report = await reporter.ReportAsync(cancellationToken);

        logger.LogReport(report);

        var problems = new List<string>();

        if (report.Unreadable.Count > 0)
        {
            problems.Add(
                $"Failed to read the logs of {report.Unreadable.Count} source(s): " +
                string.Join("; ", report.Unreadable.Select(source => $"{source.Source.DisplayName}: {source.Failure}")));
        }

        try
        {
            await notifier.PostAsync(report, cancellationToken);
        }
        // An HttpClient timeout also surfaces as TaskCanceledException, so only treat cancellation
        // as fatal when it is our own token that was cancelled.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to post the pending consent sync report to Slack.");
            problems.Add($"Slack notification failed: {ex.Message}");
        }

        if (problems.Count > 0)
        {
            // Surface as a failed invocation, so logs we can no longer read, or a channel we can no
            // longer post to, is alertable. A missing or failed run of the sync is not a problem
            // here: that is what the message reports, and this function has then done its job.
            throw new InvalidOperationException(string.Join(" | ", problems));
        }
    }
}
