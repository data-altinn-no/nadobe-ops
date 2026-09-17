namespace Nadobe.Ops.Functions.KeyVault;

public enum KeyVaultItemKind
{
    Secret,
    Certificate,
}

/// <param name="ExpiresOn">Null when no expiry date is set on the item.</param>
public sealed record KeyVaultItem(
    string VaultName,
    string Name,
    KeyVaultItemKind Kind,
    DateTimeOffset? ExpiresOn,
    bool Enabled);

/// <param name="DaysUntilExpiry">Negative when the item has already expired.</param>
public sealed record ExpiringKeyVaultItem(KeyVaultItem Item, double DaysUntilExpiry)
{
    public bool HasExpired => DaysUntilExpiry < 0;
}

public sealed record KeyVaultScanFailure(string VaultName, string Reason);

public sealed record KeyVaultExpiryReport(
    IReadOnlyList<ExpiringKeyVaultItem> Expiring,
    IReadOnlyList<KeyVaultScanFailure> Failures,
    int ItemsScanned,
    int WarningDays,
    DateTimeOffset ScannedAtUtc);
