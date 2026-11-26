using Microsoft.Extensions.Logging;

namespace Nadobe.Ops.Functions.PendingConsentSync;

public static class PendingConsentSyncReportLogging
{
    /// <summary>
    /// Writes one line per run and per source without one, so both the scheduled report and a manual
    /// one land in the same query, whether or not Slack was reached.
    /// </summary>
    public static void LogReport(this ILogger logger, PendingConsentSyncReport report)
    {
        foreach (var source in report.Sources)
        {
            if (source.IsMissing)
            {
                logger.LogError(
                    "No completed pending consent sync run in the logs of {SourceName} between {From:u} and {To:u}.",
                    source.Source.DisplayName, report.From, report.To);
            }

            foreach (var run in source.Runs)
            {
                var level = run.WasStopped ? LogLevel.Error : run.Failed > 0 ? LogLevel.Warning : LogLevel.Information;

                logger.Log(
                    level,
                    "Pending consent sync in {SourceName} completed at {CompletedAt:u}: checked {Checked}, granted {Granted}, denied {Denied}, failed {Failed}, not checked {NotChecked}, took {DurationMs} ms.",
                    source.Source.DisplayName,
                    run.CompletedAt,
                    run.Checked,
                    run.Granted,
                    run.Denied,
                    run.Failed,
                    run.NotChecked,
                    run.Duration?.TotalMilliseconds);
            }
        }
    }
}
