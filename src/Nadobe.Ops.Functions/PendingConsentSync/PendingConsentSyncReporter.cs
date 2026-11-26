using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nadobe.Ops.Functions.AppInsights;

namespace Nadobe.Ops.Functions.PendingConsentSync;

/// <summary>
/// Reads what the pending consent sync in Core logged in each configured environment and reports the
/// runs that completed, or that none did.
/// </summary>
public sealed class PendingConsentSyncReporter(
    ITraceLogReader reader,
    IOptions<PendingConsentSyncReportOptions> options,
    TimeProvider timeProvider,
    ILogger<PendingConsentSyncReporter> logger)
{
    public Task<PendingConsentSyncReport> ReportAsync(CancellationToken cancellationToken) =>
        ReportAsync(lookbackMinutesOverride: null, cancellationToken);

    /// <param name="lookbackMinutesOverride">
    /// Widens or narrows the window for a single report; falls back to the configured value when null.
    /// </param>
    public async Task<PendingConsentSyncReport> ReportAsync(int? lookbackMinutesOverride, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var sources = settings.GetSources();
        var to = timeProvider.GetUtcNow();
        var from = to.AddMinutes(-(lookbackMinutesOverride ?? settings.PendingConsentSyncReportLookbackMinutes));

        if (sources.Count == 0)
        {
            logger.LogWarning(
                "No log sources configured; set the {SettingName} app settings to the Application Insights instances to report on.",
                nameof(PendingConsentSyncReportOptions.PendingConsentSyncLogSources));

            return new PendingConsentSyncReport([], from, to);
        }

        var reports = new List<PendingConsentSyncSourceReport>(sources.Count);

        foreach (var source in sources)
        {
            try
            {
                var lines = await reader.ReadAsync(
                    source, PendingConsentSyncLogParser.MessagePrefixes, from, to, cancellationToken);

                reports.Add(new PendingConsentSyncSourceReport(source, PendingConsentSyncLogParser.Parse(lines)));
            }
            // An HttpClient timeout also surfaces as TaskCanceledException, so only treat cancellation
            // as fatal when it is our own token that was cancelled.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One unreadable environment should not hide the runs in the others.
                logger.LogError(ex, "Failed to read the logs of {SourceName}.", source.DisplayName);
                reports.Add(new PendingConsentSyncSourceReport(source, [], ex.Message));
            }
        }

        return new PendingConsentSyncReport(reports, from, to);
    }
}
