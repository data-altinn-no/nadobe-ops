namespace Nadobe.Ops.Functions.KeyVault;

/// <summary>
/// Bound from the app settings root, so each property maps to a flat app setting of the same name.
/// </summary>
public sealed class KeyVaultExpiryOptions
{
    /// <summary>
    /// Comma-separated vault names, or full vault URIs. An entry may carry a display alias after an
    /// equals sign (<c>kv-contoso-prod-001=prod</c>); messages then show the alias instead of the name.
    /// </summary>
    public string KeyVaultNames { get; set; } = string.Empty;

    /// <summary>Report items expiring within this many days. Items already expired are always reported.</summary>
    public int KeyVaultExpiryWarningDays { get; set; } = 40;

    /// <summary>DNS suffix used to build a vault URI from a bare vault name. Override for sovereign clouds.</summary>
    public string KeyVaultDnsSuffix { get; set; } = "vault.azure.net";

    /// <summary>The configured vaults in the order given, deduplicated by name.</summary>
    public IReadOnlyList<KeyVaultTarget> GetVaults()
    {
        var targets = new List<KeyVaultTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in KeyVaultNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = entry.IndexOf('=');
            var name = separator < 0 ? entry : entry[..separator].Trim();
            var alias = separator < 0 ? string.Empty : entry[(separator + 1)..].Trim();

            if (name.Length == 0 || !seen.Add(name))
            {
                continue;
            }

            targets.Add(new KeyVaultTarget(name, alias.Length > 0 ? alias : name));
        }

        return targets;
    }

    public IReadOnlyList<string> GetVaultNames() => [.. GetVaults().Select(vault => vault.Name)];

    /// <summary>Accepts either a bare vault name ("my-vault") or a full URI ("https://my-vault.vault.azure.net/").</summary>
    public Uri BuildVaultUri(string vaultNameOrUri) =>
        Uri.TryCreate(vaultNameOrUri, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri($"https://{vaultNameOrUri}.{KeyVaultDnsSuffix}/");
}

/// <param name="Name">Vault name or URI as used to connect.</param>
/// <param name="DisplayName">Short label for messages; equals <paramref name="Name"/> when no alias is configured.</param>
public sealed record KeyVaultTarget(string Name, string DisplayName);
