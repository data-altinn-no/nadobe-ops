using Nadobe.Ops.Functions.AppInsights;

namespace Nadobe.Ops.Functions.PendingConsentSync;

/// <summary>
/// Bound from the app settings root. The log sources are a list, so each one is a pair of indexed app
/// settings: <c>PendingConsentSyncLogSources__0__Name</c> and <c>PendingConsentSyncLogSources__0__ResourceId</c>,
/// then <c>__1__</c> for the next.
/// </summary>
public sealed class PendingConsentSyncReportOptions
{
    /// <summary>The Application Insights instances Core logs to, one per environment to report on.</summary>
    public List<LogSource> PendingConsentSyncLogSources { get; set; } = [];

    /// <summary>
    /// How far back from the time of the report to look for completed runs. Has to cover the delay
    /// between the schedule of the sync and that of the report, plus a margin.
    /// </summary>
    public int PendingConsentSyncReportLookbackMinutes { get; set; } = 45;

    /// <summary>The configured sources in the order given, without incomplete or repeated entries.</summary>
    public IReadOnlyList<LogSource> GetSources()
    {
        var sources = new List<LogSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in PendingConsentSyncLogSources)
        {
            var resourceId = entry.ResourceId.Trim();

            // An entry with only a name is a half-finished app setting, not something to query.
            if (resourceId.Length == 0 || !seen.Add(resourceId))
            {
                continue;
            }

            sources.Add(new LogSource { Name = entry.Name.Trim(), ResourceId = resourceId });
        }

        return sources;
    }
}
