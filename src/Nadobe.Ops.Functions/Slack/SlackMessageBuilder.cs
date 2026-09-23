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
            // The report is sorted by urgency, so cutting the head keeps the most pressing findings
            // whichever vault they are in. Grouping happens after the cut for the same reason.
            var listed = report.Expiring.Take(MaxListedFindings).ToList();
            var first = true;

            foreach (var vault in GroupByVault(listed, report.Vaults ?? []))
            {
                if (!first)
                {
                    blocks.Add(SlackBlock.Divider());
                }

                first = false;
                blocks.AddRange(ToSections(FormatVault(vault)));
            }

            if (report.Expiring.Count > listed.Count)
            {
                blocks.Add(SlackBlock.Section(
                    $"_…and {report.Expiring.Count - listed.Count} more, see the logs for the full list._"));
            }
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

        // Expired and approaching items are disjoint, so the header counts them separately rather
        // than reporting the total as "expiring" and then repeating the expired share of it.
        var expired = report.Expiring.Count(entry => entry.HasExpired);
        var approaching = report.Expiring.Count - expired;
        var parts = new List<string>(3);

        if (expired > 0)
        {
            parts.Add($"{expired} already expired");
        }

        if (approaching > 0)
        {
            parts.Add($"{approaching} expiring within {report.WarningDays} days");
        }

        if (report.Failures.Count > 0)
        {
            parts.Add($"{report.Failures.Count} vault(s) unreadable");
        }

        return "Key Vault expiry: " + string.Join(", ", parts);
    }

    /// <summary>
    /// One group per vault, in the configured order. Vaults missing from the configuration (which
    /// only happens in tests) follow in order of their most urgent finding.
    /// </summary>
    private static IEnumerable<IGrouping<string, ExpiringKeyVaultItem>> GroupByVault(
        IEnumerable<ExpiringKeyVaultItem> entries,
        IReadOnlyList<KeyVaultTarget> vaults)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var vault in vaults)
        {
            rank.TryAdd(vault.Name, rank.Count);
        }

        // OrderBy is stable, so unranked groups keep their first-appearance order.
        return entries
            .GroupBy(entry => entry.Item.VaultName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => rank.TryGetValue(group.Key, out var index) ? index : int.MaxValue);
    }

    /// <summary>The vault's label, then its certificates and secrets under their own headings.</summary>
    private static IEnumerable<string> FormatVault(IGrouping<string, ExpiringKeyVaultItem> vault)
    {
        yield return $"*{Escape(vault.First().VaultLabel)}*";

        var byKind = vault
            .GroupBy(entry => entry.Item.Kind)
            .OrderBy(kind => kind.Key == KeyVaultItemKind.Certificate ? 0 : 1);

        foreach (var kind in byKind)
        {
            yield return $"_{Heading(kind.Key)}_";

            foreach (var entry in kind)
            {
                yield return FormatFinding(entry);
            }
        }
    }

    private static string Heading(KeyVaultItemKind kind) => kind switch
    {
        KeyVaultItemKind.Certificate => "Certificates",
        KeyVaultItemKind.Secret => "Secrets",
        _ => kind.ToString(),
    };

    private static string FormatFinding(ExpiringKeyVaultItem entry)
    {
        var icon = entry.HasExpired ? ":red_circle:" : ":large_yellow_circle:";
        var days = (int)Math.Floor(Math.Abs(entry.DaysUntilExpiry));
        var timing = entry.HasExpired ? $"expired {days} day(s) ago" : $"expires in {days} day(s)";

        return $"{icon} *{Escape(entry.Item.Name)}* — {timing} ({entry.Item.ExpiresOn:yyyy-MM-dd})";
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
