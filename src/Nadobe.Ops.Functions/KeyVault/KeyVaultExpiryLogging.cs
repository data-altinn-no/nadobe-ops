using Microsoft.Extensions.Logging;

namespace Nadobe.Ops.Functions.KeyVault;

public static class KeyVaultExpiryLogging
{
    /// <summary>Writes one line per finding, so both the scheduled run and a manual run land in the same query.</summary>
    public static void LogFindings(this ILogger logger, KeyVaultExpiryReport report)
    {
        foreach (var entry in report.Expiring)
        {
            if (entry.HasExpired)
            {
                logger.LogError(
                    "{Kind} {Name} in vault {VaultName} expired {Days:F0} days ago ({ExpiresOn:u}).",
                    entry.Item.Kind, entry.Item.Name, entry.Item.VaultName, -entry.DaysUntilExpiry, entry.Item.ExpiresOn);
            }
            else
            {
                logger.LogWarning(
                    "{Kind} {Name} in vault {VaultName} expires in {Days:F0} days ({ExpiresOn:u}).",
                    entry.Item.Kind, entry.Item.Name, entry.Item.VaultName, entry.DaysUntilExpiry, entry.Item.ExpiresOn);
            }
        }
    }
}
