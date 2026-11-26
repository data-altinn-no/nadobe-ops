using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.PendingConsentSync;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

internal sealed class FakeSlackNotifier : ISlackNotifier
{
    public List<KeyVaultExpiryReport> Posted { get; } = [];

    public List<PendingConsentSyncReport> PostedSyncReports { get; } = [];

    public SlackPostStatus Result { get; set; } = SlackPostStatus.Posted;

    public Exception? ThrowOnPost { get; set; }

    public Task<SlackPostStatus> PostAsync(KeyVaultExpiryReport report, CancellationToken cancellationToken)
    {
        if (ThrowOnPost is not null)
        {
            throw ThrowOnPost;
        }

        Posted.Add(report);
        return Task.FromResult(Result);
    }

    public Task<SlackPostStatus> PostAsync(PendingConsentSyncReport report, CancellationToken cancellationToken)
    {
        if (ThrowOnPost is not null)
        {
            throw ThrowOnPost;
        }

        PostedSyncReports.Add(report);
        return Task.FromResult(Result);
    }
}
