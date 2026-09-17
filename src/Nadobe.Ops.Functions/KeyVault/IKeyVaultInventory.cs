namespace Nadobe.Ops.Functions.KeyVault;

/// <summary>
/// Lists the secrets and certificates of a single vault. Kept deliberately thin so that
/// <see cref="KeyVaultExpiryScanner"/> can be tested without touching Azure.
/// </summary>
public interface IKeyVaultInventory
{
    IAsyncEnumerable<KeyVaultItem> EnumerateAsync(string vaultName, CancellationToken cancellationToken);
}
