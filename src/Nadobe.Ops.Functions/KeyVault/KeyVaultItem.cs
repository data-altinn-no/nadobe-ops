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
/// <param name="VaultDisplayName">The vault's configured alias; null falls back to the vault name.</param>
public sealed record ExpiringKeyVaultItem(KeyVaultItem Item, double DaysUntilExpiry, string? VaultDisplayName = null)
{
    public bool HasExpired => DaysUntilExpiry < 0;

    /// <summary>What messages call the vault. Logs keep using the real name.</summary>
    public string VaultLabel => VaultDisplayName ?? Item.VaultName;
}

public sealed record KeyVaultScanFailure(string VaultName, string Reason);

/// <param name="Vaults">The configured vaults in order, so messages can group findings the same way.</param>
public sealed record KeyVaultExpiryReport(
    IReadOnlyList<ExpiringKeyVaultItem> Expiring,
    IReadOnlyList<KeyVaultScanFailure> Failures,
    int ItemsScanned,
    int WarningDays,
    DateTimeOffset ScannedAtUtc,
    IReadOnlyList<KeyVaultTarget>? Vaults = null);
