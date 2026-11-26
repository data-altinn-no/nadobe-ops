using System.Globalization;
using Nadobe.Ops.Functions.PendingConsentSync;
using static Nadobe.Ops.Functions.Slack.SlackFormatting;

namespace Nadobe.Ops.Functions.Slack;

/// <summary>
/// Turns a pending consent sync report into a Slack message. Pure so the formatting can be tested
/// without a webhook.
/// </summary>
public static class PendingConsentSyncMessageBuilder
{
    /// <param name="mention">
    /// Slack mention markup such as <c>&lt;!subteam^ID&gt;</c> or <c>&lt;!here&gt;</c>, added only when
    /// something needs attention. Passed through unescaped, since escaping would defeat it.
    /// </param>
    public static SlackMessage Build(PendingConsentSyncReport report, string? mention = null)
    {
        var summary = report.HasProblems
            ? "⚠️ Pending consent sync needs attention"
            : "✅ Pending consent sync completed";
        var blocks = new List<SlackBlock> { SlackBlock.Header(summary) };
        var text = summary;

        if (report.HasProblems && !string.IsNullOrWhiteSpace(mention))
        {
            // Mentions only resolve in mrkdwn, so this cannot go in the plain_text header. The
            // fallback text carries it too, so the push notification shows who was mentioned.
            blocks.Add(SlackBlock.Section($"{mention.Trim()} — the sync needs attention, see below."));
            text = $"{mention.Trim()} {summary}";
        }

        blocks.AddRange(ToSections(report.Sources.SelectMany(FormatSource)));

        blocks.Add(SlackBlock.Section(
            string.Create(CultureInfo.InvariantCulture, $"_Runs completed between {report.From:u} and {report.To:u}._")));

        return new SlackMessage(text, blocks);
    }

    private static IEnumerable<string> FormatSource(PendingConsentSyncSourceReport source)
    {
        var name = $"*{Escape(source.Source.DisplayName)}*";

        if (source.Failure is { } failure)
        {
            // Not the same as a missing run: nothing is known about the sync when its logs are unreadable.
            yield return $":warning: Could not read the logs for {name}: {Escape(failure)}";
            yield break;
        }

        if (source.IsMissing)
        {
            yield return $":red_circle: {name} — no completed run found in the logs";
        }

        foreach (var run in source.Runs)
        {
            yield return FormatRun(name, run);
        }
    }

    private static string FormatRun(string name, PendingConsentSyncRun run)
    {
        var when = run.CompletedAt.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        var took = run.Duration is { } duration ? $" · took {FormatDuration(duration)}" : string.Empty;

        if (run.WasStopped)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $":red_circle: {name} {when} UTC — stopped after repeated failures, *{run.NotChecked} not checked*. Checked {run.Checked}, granted {run.Granted}, denied {run.Denied}, failed {run.Failed}{took}");
        }

        var icon = run.Failed > 0 ? ":warning:" : ":white_check_mark:";
        var failed = run.Failed > 0 ? $"*failed {run.Failed}*" : "failed 0";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{icon} {name} {when} UTC — checked {run.Checked}, granted {run.Granted}, denied {run.Denied}, {failed}{took}");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromSeconds(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(long)Math.Round(duration.TotalMilliseconds)} ms");
        }

        if (duration < TimeSpan.FromMinutes(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalSeconds} s");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds} s");
    }
}
