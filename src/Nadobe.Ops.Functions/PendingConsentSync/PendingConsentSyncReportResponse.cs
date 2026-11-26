using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.PendingConsentSync;

/// <summary>Serialisable shape of <see cref="PendingConsentSyncReport"/> for the manual-trigger endpoint.</summary>
/// <param name="SlackStatus">A <see cref="SlackPostStatus"/> name, so an unposted report says why.</param>
public sealed record PendingConsentSyncReportResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PendingConsentSyncSourceResponse> Sources,
    string SlackStatus,
    string? SlackError)
{
    public static PendingConsentSyncReportResponse Create(
        PendingConsentSyncReport report,
        SlackPostStatus slackStatus,
        string? slackError) =>
        new(
            report.From,
            report.To,
            [.. report.Sources.Select(PendingConsentSyncSourceResponse.Create)],
            slackStatus.ToString(),
            slackError);
}

/// <param name="MissingRun">The logs were readable and hold no completed run.</param>
/// <param name="Failure">Why the logs could not be read; null when they could.</param>
public sealed record PendingConsentSyncSourceResponse(
    string Name,
    string ResourceId,
    IReadOnlyList<PendingConsentSyncRun> Runs,
    bool MissingRun,
    string? Failure)
{
    public static PendingConsentSyncSourceResponse Create(PendingConsentSyncSourceReport source) =>
        new(source.Source.DisplayName, source.Source.ResourceId, source.Runs, source.IsMissing, source.Failure);
}
