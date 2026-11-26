using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.PendingConsentSync;

namespace Nadobe.Ops.Functions.Slack;

public interface ISlackNotifier
{
    /// <summary>
    /// Posts the report to Slack. Returns <see cref="SlackPostStatus.Posted"/>, or the reason nothing was
    /// sent; throws when a post was attempted and failed.
    /// </summary>
    Task<SlackPostStatus> PostAsync(KeyVaultExpiryReport report, CancellationToken cancellationToken);

    /// <summary>
    /// Posts the report to Slack, whether or not the runs in it went well. Returns
    /// <see cref="SlackPostStatus.Posted"/>, or the reason nothing was sent; throws when a post was
    /// attempted and failed.
    /// </summary>
    Task<SlackPostStatus> PostAsync(PendingConsentSyncReport report, CancellationToken cancellationToken);
}
