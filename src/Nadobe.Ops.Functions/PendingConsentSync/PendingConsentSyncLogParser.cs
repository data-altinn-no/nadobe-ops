using System.Globalization;
using System.Text.RegularExpressions;
using Nadobe.Ops.Functions.AppInsights;

namespace Nadobe.Ops.Functions.PendingConsentSync;

/// <summary>
/// Turns the lines the pending consent sync in Core logs into runs. Pure, so it can be tested against
/// log lines copied from Application Insights. The rendered message is parsed rather than the
/// structured properties, since the message reads the same whichever way Core exports its logs.
/// </summary>
public static partial class PendingConsentSyncLogParser
{
    /// <summary>The line with the counts, logged once at the end of every run.</summary>
    public const string SummaryPrefix = "Completed pending consent sync";

    /// <summary>The line with the run time, logged by the timer helper in Core just before the summary.</summary>
    public const string TimerPrefix = "[NadobeTimer] pending-consent-sync";

    /// <summary>The message prefixes to ask the log source for.</summary>
    public static IReadOnlyList<string> MessagePrefixes { get; } = [SummaryPrefix, TimerPrefix];

    /// <summary>The runs in <paramref name="lines"/>, oldest first.</summary>
    public static IReadOnlyList<PendingConsentSyncRun> Parse(IEnumerable<TraceLine> lines)
    {
        var durations = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        var summaries = new List<(TraceLine Line, Match Match)>();

        foreach (var line in lines)
        {
            var timer = TimerLine().Match(line.Message);

            if (timer.Success)
            {
                // Without an operation id there is nothing to say which run a timer line belongs to.
                if (line.OperationId.Length > 0 && TryParse(timer.Groups["elapsedMs"], out long elapsedMs))
                {
                    durations[line.OperationId] = TimeSpan.FromMilliseconds(elapsedMs);
                }

                continue;
            }

            var summary = SummaryLine().Match(line.Message);

            if (summary.Success)
            {
                summaries.Add((line, summary));
            }
        }

        var runs = new List<PendingConsentSyncRun>(summaries.Count);

        foreach (var (line, match) in summaries)
        {
            if (!TryParse(match.Groups["checked"], out int checkedCount) ||
                !TryParse(match.Groups["granted"], out int granted) ||
                !TryParse(match.Groups["denied"], out int denied) ||
                !TryParse(match.Groups["failed"], out int failed))
            {
                continue;
            }

            // The first version of the sync did not log notChecked.
            var notChecked = 0;

            if (match.Groups["notChecked"].Success && !TryParse(match.Groups["notChecked"], out notChecked))
            {
                continue;
            }

            TimeSpan? duration = durations.TryGetValue(line.OperationId, out var found) ? found : null;

            runs.Add(new PendingConsentSyncRun(line.Timestamp, checkedCount, granted, denied, failed, notChecked, duration));
        }

        // OrderBy is stable, so runs logged at the same instant keep the order they were read in.
        return [.. runs.OrderBy(run => run.CompletedAt)];
    }

    private static bool TryParse<T>(Group group, out T value)
        where T : struct, ISpanParsable<T>
    {
        value = default;
        return group.Success && T.TryParse(group.ValueSpan, CultureInfo.InvariantCulture, out value);
    }

    [GeneratedRegex(
        @"^Completed pending consent sync checked=(?<checked>[0-9]+) granted=(?<granted>[0-9]+) denied=(?<denied>[0-9]+) failed=(?<failed>[0-9]+)(?: notChecked=(?<notChecked>[0-9]+))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex SummaryLine();

    [GeneratedRegex(@"^\[NadobeTimer\] pending-consent-sync elapsedMs=(?<elapsedMs>[0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TimerLine();
}
