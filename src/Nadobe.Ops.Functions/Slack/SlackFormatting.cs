using System.Text;

namespace Nadobe.Ops.Functions.Slack;

/// <summary>What every message builder needs to stay within the rules Slack sets for mrkdwn sections.</summary>
internal static class SlackFormatting
{
    /// <summary>Slack rejects section blocks over 3000 characters.</summary>
    private const int MaxSectionLength = 2900;

    /// <summary>Packs lines into as few section blocks as the per-block length limit allows.</summary>
    public static IEnumerable<SlackBlock> ToSections(IEnumerable<string> lines)
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

    public static string Escape(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
