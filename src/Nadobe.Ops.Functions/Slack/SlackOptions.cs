namespace Nadobe.Ops.Functions.Slack;

/// <summary>
/// Bound from the app settings root, so each property maps to a flat app setting of the same name.
/// </summary>
public sealed class SlackOptions
{
    /// <summary>
    /// Slack incoming-webhook URL. This is a credential: store it as a Key Vault reference, never in
    /// source. Leaving it empty disables Slack posting entirely.
    /// </summary>
    public string SlackWebhookUrl { get; set; } = string.Empty;

    /// <summary>Post a "nothing expiring" message too, so a silent channel means a broken job.</summary>
    public bool SlackPostWhenNoFindings { get; set; }

    /// <summary>
    /// Slack mention markup to add when there are expiring items: <c>&lt;!subteam^S0123ABCDEF&gt;</c>
    /// for a user group (the ID is in the group's URL in Slack) or <c>&lt;!here&gt;</c>. Plain "@name"
    /// text does not notify anyone. Empty disables mentions.
    /// </summary>
    public string SlackMentionOnFindings { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SlackWebhookUrl);
}
