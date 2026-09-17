using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nadobe.Ops.Functions.KeyVault;

public sealed class KeyVaultExpiryScanner(
    IKeyVaultInventory inventory,
    IOptions<KeyVaultExpiryOptions> options,
    TimeProvider timeProvider,
    ILogger<KeyVaultExpiryScanner> logger)
{
    public Task<KeyVaultExpiryReport> ScanAsync(CancellationToken cancellationToken) =>
        ScanAsync(warningDaysOverride: null, cancellationToken);

    /// <param name="warningDaysOverride">
    /// Widens or narrows the window for a single scan; falls back to the configured value when null.
    /// </param>
    public async Task<KeyVaultExpiryReport> ScanAsync(int? warningDaysOverride, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var warningDays = warningDaysOverride ?? settings.KeyVaultExpiryWarningDays;
        var vaultNames = settings.GetVaultNames();
        var now = timeProvider.GetUtcNow();

        if (vaultNames.Count == 0)
        {
            logger.LogWarning(
                "No vaults configured; set the {SettingName} app setting to a comma-separated list of vault names.",
                nameof(KeyVaultExpiryOptions.KeyVaultNames));

            return new KeyVaultExpiryReport([], [], 0, warningDays, now);
        }

        var threshold = now.AddDays(warningDays);

        var expiring = new List<ExpiringKeyVaultItem>();
        var failures = new List<KeyVaultScanFailure>();
        var itemsScanned = 0;

        foreach (var vaultName in vaultNames)
        {
            try
            {
                await foreach (var item in inventory.EnumerateAsync(vaultName, cancellationToken))
                {
                    itemsScanned++;

                    // Disabled items cannot be used, so an expiry on them is not actionable.
                    if (!item.Enabled || item.ExpiresOn is not { } expiresOn || expiresOn > threshold)
                    {
                        continue;
                    }

                    expiring.Add(new ExpiringKeyVaultItem(item, (expiresOn - now).TotalDays));
                }
            }
            // An HttpClient timeout also surfaces as TaskCanceledException, so only treat cancellation
            // as fatal when it is our own token that was cancelled.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One unreachable vault should not hide findings in the others.
                logger.LogError(ex, "Failed to scan vault {VaultName}.", vaultName);
                failures.Add(new KeyVaultScanFailure(vaultName, ex.Message));
            }
        }

        expiring.Sort(static (left, right) => left.DaysUntilExpiry.CompareTo(right.DaysUntilExpiry));

        return new KeyVaultExpiryReport(expiring, failures, itemsScanned, warningDays, now);
    }
}
