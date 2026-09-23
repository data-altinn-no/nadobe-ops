using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.KeyVault;

/// <summary>Serialisable shape of <see cref="KeyVaultExpiryReport"/> for the manual-trigger endpoint.</summary>
/// <param name="SlackStatus">A <see cref="SlackPostStatus"/> name, so an unposted run says why.</param>
public sealed record KeyVaultExpiryResponse(
    DateTimeOffset ScannedAtUtc,
    int WarningDays,
    int ItemsScanned,
    IReadOnlyList<ExpiringItemResponse> Expiring,
    IReadOnlyList<KeyVaultScanFailure> Failures,
    string SlackStatus,
    string? SlackError)
{
    public static KeyVaultExpiryResponse From(KeyVaultExpiryReport report, SlackPostStatus slackStatus, string? slackError) =>
        new(
            report.ScannedAtUtc,
            report.WarningDays,
            report.ItemsScanned,
            [.. report.Expiring.Select(ExpiringItemResponse.From)],
            report.Failures,
            slackStatus.ToString(),
            slackError);
}

public sealed record ExpiringItemResponse(
    string VaultName,
    string VaultDisplayName,
    string Name,
    string Kind,
    DateTimeOffset? ExpiresOn,
    int DaysUntilExpiry,
    bool Expired)
{
    public static ExpiringItemResponse From(ExpiringKeyVaultItem entry) =>
        new(
            entry.Item.VaultName,
            entry.VaultLabel,
            entry.Item.Name,
            entry.Item.Kind.ToString(),
            entry.Item.ExpiresOn,
            (int)Math.Floor(entry.DaysUntilExpiry),
            entry.HasExpired);
}
