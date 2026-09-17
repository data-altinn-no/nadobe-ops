using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Slack;

public interface ISlackNotifier
{
    /// <summary>
    /// Posts the report to Slack. Returns false when posting is disabled or the report is not worth
    /// posting; throws when a post was attempted and failed.
    /// </summary>
    Task<bool> PostAsync(KeyVaultExpiryReport report, CancellationToken cancellationToken);
}
