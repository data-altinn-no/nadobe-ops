namespace Nadobe.Ops.Functions.KeyVault;

/// <summary>
/// Bound from the app settings root, so each property maps to a flat app setting of the same name.
/// </summary>
public sealed class KeyVaultExpiryOptions
{
    /// <summary>Comma-separated vault names, or full vault URIs.</summary>
    public string KeyVaultNames { get; set; } = string.Empty;

    /// <summary>Report items expiring within this many days. Items already expired are always reported.</summary>
    public int KeyVaultExpiryWarningDays { get; set; } = 40;

    /// <summary>DNS suffix used to build a vault URI from a bare vault name. Override for sovereign clouds.</summary>
    public string KeyVaultDnsSuffix { get; set; } = "vault.azure.net";

    public IReadOnlyList<string> GetVaultNames() =>
        KeyVaultNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Accepts either a bare vault name ("my-vault") or a full URI ("https://my-vault.vault.azure.net/").</summary>
    public Uri BuildVaultUri(string vaultNameOrUri) =>
        Uri.TryCreate(vaultNameOrUri, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri($"https://{vaultNameOrUri}.{KeyVaultDnsSuffix}/");
}
