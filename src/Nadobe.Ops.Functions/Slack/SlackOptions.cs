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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SlackWebhookUrl);
}
