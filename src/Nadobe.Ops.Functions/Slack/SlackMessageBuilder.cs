using System.Text;
using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Slack;

/// <summary>
/// Turns a scan report into a Slack message. Pure so the formatting can be tested without a webhook.
/// </summary>
public static class SlackMessageBuilder
{
    /// <summary>Long lists are truncated; the full set is always in the logs.</summary>
    public const int MaxListedFindings = 25;

    /// <summary>Slack rejects section blocks over 3000 characters.</summary>
    private const int MaxSectionLength = 2900;

    public static SlackMessage Build(KeyVaultExpiryReport report)
    {
        var summary = BuildSummary(report);
        var blocks = new List<SlackBlock> { SlackBlock.Header(summary) };

        if (report.Expiring.Count > 0)
        {
            var lines = report.Expiring
                .Take(MaxListedFindings)
                .Select(FormatFinding)
                .ToList();

            if (report.Expiring.Count > MaxListedFindings)
            {
                lines.Add($"_…and {report.Expiring.Count - MaxListedFindings} more, see the logs for the full list._");
            }

            blocks.AddRange(ToSections(lines));
        }

        if (report.Failures.Count > 0)
        {
            var lines = report.Failures
                .Select(failure => $":warning: Could not read vault `{Escape(failure.VaultName)}`: {Escape(failure.Reason)}")
                .ToList();

            blocks.AddRange(ToSections(lines));
        }

        blocks.Add(SlackBlock.Section(
            $"_Scanned {report.ItemsScanned} item(s) with a {report.WarningDays}-day window at {report.ScannedAtUtc:u}._"));

        return new SlackMessage(summary, blocks);
    }

    private static string BuildSummary(KeyVaultExpiryReport report)
    {
        if (report.Expiring.Count == 0)
        {
            return report.Failures.Count > 0
                ? $"Key Vault expiry: no findings, {report.Failures.Count} vault(s) unreadable"
                : "Key Vault expiry: nothing expiring";
        }

        var expired = report.Expiring.Count(entry => entry.HasExpired);
        var summary = $"Key Vault expiry: {report.Expiring.Count} item(s) expiring within {report.WarningDays} days";

        if (expired > 0)
        {
            summary += $", {expired} already expired";
        }

        return summary;
    }

    private static string FormatFinding(ExpiringKeyVaultItem entry)
    {
        var icon = entry.HasExpired ? ":red_circle:" : ":large_yellow_circle:";
        var days = (int)Math.Floor(Math.Abs(entry.DaysUntilExpiry));
        var timing = entry.HasExpired ? $"expired {days} day(s) ago" : $"expires in {days} day(s)";

        return $"{icon} *{Escape(entry.Item.Name)}* ({entry.Item.Kind}) in `{Escape(entry.Item.VaultName)}` — " +
               $"{timing} ({entry.Item.ExpiresOn:yyyy-MM-dd})";
    }

    /// <summary>Packs lines into as few section blocks as Slack's per-block length allows.</summary>
    private static IEnumerable<SlackBlock> ToSections(IEnumerable<string> lines)
    {
        var current = new StringBuilder();

        foreach (var line in lines)
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > MaxSectionLength)
            {
                yield return SlackBlock.Section(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append('\n');
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            yield return SlackBlock.Section(current.ToString());
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
