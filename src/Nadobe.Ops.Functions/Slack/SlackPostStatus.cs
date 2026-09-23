namespace Nadobe.Ops.Functions.Slack;

/// <summary>
/// Why a report did or did not reach Slack. Returned by <see cref="ISlackNotifier"/> and surfaced
/// verbatim by the manual-trigger endpoint, so "nothing was posted" is never ambiguous.
/// </summary>
public enum SlackPostStatus
{
    /// <summary>The webhook accepted the message.</summary>
    Posted,

    /// <summary>The caller did not ask for a post; the manual endpoint only posts with <c>notify=true</c>.</summary>
    NotRequested,

    /// <summary><c>SlackWebhookUrl</c> is not set, so posting is disabled.</summary>
    NotConfigured,

    /// <summary>No findings and <c>SlackPostWhenNoFindings</c> is off, so there was nothing to say.</summary>
    NothingToPost,

    /// <summary>A post was attempted and failed; the reason is in <c>slackError</c>.</summary>
    Failed,
}
