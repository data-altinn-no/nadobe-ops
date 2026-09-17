using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

internal sealed class FakeSlackNotifier : ISlackNotifier
{
    public List<KeyVaultExpiryReport> Posted { get; } = [];

    public bool Result { get; set; } = true;

    public Exception? ThrowOnPost { get; set; }

    public Task<bool> PostAsync(KeyVaultExpiryReport report, CancellationToken cancellationToken)
    {
        if (ThrowOnPost is not null)
        {
            throw ThrowOnPost;
        }

        Posted.Add(report);
        return Task.FromResult(Result);
    }
}
