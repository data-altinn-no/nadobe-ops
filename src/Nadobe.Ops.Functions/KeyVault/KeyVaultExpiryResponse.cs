namespace Nadobe.Ops.Functions.KeyVault;

/// <summary>Serialisable shape of <see cref="KeyVaultExpiryReport"/> for the manual-trigger endpoint.</summary>
public sealed record KeyVaultExpiryResponse(
    DateTimeOffset ScannedAtUtc,
    int WarningDays,
    int ItemsScanned,
    IReadOnlyList<ExpiringItemResponse> Expiring,
    IReadOnlyList<KeyVaultScanFailure> Failures,
    bool PostedToSlack,
    string? SlackError)
{
    public static KeyVaultExpiryResponse From(KeyVaultExpiryReport report, bool postedToSlack, string? slackError) =>
        new(
            report.ScannedAtUtc,
            report.WarningDays,
            report.ItemsScanned,
            [.. report.Expiring.Select(ExpiringItemResponse.From)],
            report.Failures,
            postedToSlack,
            slackError);
}

public sealed record ExpiringItemResponse(
    string VaultName,
    string Name,
    string Kind,
    DateTimeOffset? ExpiresOn,
    int DaysUntilExpiry,
    bool Expired)
{
    public static ExpiringItemResponse From(ExpiringKeyVaultItem entry) =>
        new(
            entry.Item.VaultName,
            entry.Item.Name,
            entry.Item.Kind.ToString(),
            entry.Item.ExpiresOn,
            (int)Math.Floor(entry.DaysUntilExpiry),
            entry.HasExpired);
}
