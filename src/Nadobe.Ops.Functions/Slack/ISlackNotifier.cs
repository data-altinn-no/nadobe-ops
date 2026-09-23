using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Slack;

public interface ISlackNotifier
{
    /// <summary>
    /// Posts the report to Slack. Returns <see cref="SlackPostStatus.Posted"/>, or the reason nothing was
    /// sent; throws when a post was attempted and failed.
    /// </summary>
    Task<SlackPostStatus> PostAsync(KeyVaultExpiryReport report, CancellationToken cancellationToken);
}
