using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Tests;

public class KeyVaultExpiryScannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static KeyVaultExpiryScanner CreateScanner(IKeyVaultInventory inventory, string vaultNames, int warningDays = 40) =>
        new(
            inventory,
            Options.Create(new KeyVaultExpiryOptions
            {
                KeyVaultNames = vaultNames,
                KeyVaultExpiryWarningDays = warningDays,
            }),
            new FakeTimeProvider(Now),
            NullLogger<KeyVaultExpiryScanner>.Instance);

    private static KeyVaultItem Item(
        string name,
        double? expiresInDays,
        KeyVaultItemKind kind = KeyVaultItemKind.Secret,
        bool enabled = true,
        string vaultName = "vault-a") =>
        new(vaultName, name, kind, expiresInDays is { } days ? Now.AddDays(days) : null, enabled);

    [Fact]
    public async Task ScanAsync_ReportsOnlyItemsInsideTheWindow()
    {
        var inventory = new FakeKeyVaultInventory().WithVault(
            "vault-a",
            Item("expires-in-39-days", 39),
            Item("expires-in-41-days", 41),
            Item("cert-in-10-days", 10, KeyVaultItemKind.Certificate));

        var report = await CreateScanner(inventory, "vault-a").ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, report.ItemsScanned);
        Assert.Equal(
            ["cert-in-10-days", "expires-in-39-days"],
            report.Expiring.Select(entry => entry.Item.Name));
    }

    [Fact]
    public async Task ScanAsync_OrdersByClosestExpiryFirst()
    {
        var inventory = new FakeKeyVaultInventory().WithVault(
            "vault-a",
            Item("in-30-days", 30),
            Item("expired-5-days-ago", -5),
            Item("in-2-days", 2));

        var report = await CreateScanner(inventory, "vault-a").ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["expired-5-days-ago", "in-2-days", "in-30-days"],
            report.Expiring.Select(entry => entry.Item.Name));
    }

    [Fact]
    public async Task ScanAsync_FlagsAlreadyExpiredItems()
    {
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("stale", -3));

        var report = await CreateScanner(inventory, "vault-a").ScanAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(report.Expiring);
        Assert.True(entry.HasExpired);
        Assert.Equal(-3, entry.DaysUntilExpiry, precision: 6);
    }

    [Fact]
    public async Task ScanAsync_SkipsItemsWithoutExpiryAndDisabledItems()
    {
        var inventory = new FakeKeyVaultInventory().WithVault(
            "vault-a",
            Item("no-expiry", null),
            Item("disabled-but-expiring", 1, enabled: false));

        var report = await CreateScanner(inventory, "vault-a").ScanAsync(TestContext.Current.CancellationToken);

        Assert.Empty(report.Expiring);
        Assert.Equal(2, report.ItemsScanned);
    }

    [Fact]
    public async Task ScanAsync_ScansEveryConfiguredVault()
    {
        var inventory = new FakeKeyVaultInventory()
            .WithVault("vault-a", Item("a", 5))
            .WithVault("vault-b", Item("b", 5, vaultName: "vault-b"));

        var report = await CreateScanner(inventory, " vault-a , vault-b ").ScanAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["vault-a", "vault-b"], inventory.EnumeratedVaults);
        Assert.Equal(2, report.Expiring.Count);
    }

    [Fact]
    public async Task ScanAsync_RecordsFailureAndKeepsScanningRemainingVaults()
    {
        var inventory = new FakeKeyVaultInventory()
            .WithFailingVault("vault-a", new InvalidOperationException("forbidden"))
            .WithVault("vault-b", Item("b", 5, vaultName: "vault-b"));

        var report = await CreateScanner(inventory, "vault-a,vault-b").ScanAsync(TestContext.Current.CancellationToken);

        var failure = Assert.Single(report.Failures);
        Assert.Equal("vault-a", failure.VaultName);
        Assert.Equal("forbidden", failure.Reason);
        Assert.Single(report.Expiring);
    }

    [Fact]
    public async Task ScanAsync_WithNoVaultsConfigured_ReturnsEmptyReport()
    {
        var inventory = new FakeKeyVaultInventory();

        var report = await CreateScanner(inventory, "  ").ScanAsync(TestContext.Current.CancellationToken);

        Assert.Empty(report.Expiring);
        Assert.Empty(report.Failures);
        Assert.Empty(inventory.EnumeratedVaults);
    }

    [Fact]
    public async Task ScanAsync_HonoursConfiguredWindow()
    {
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("in-50-days", 50));

        var report = await CreateScanner(inventory, "vault-a", warningDays: 60).ScanAsync(TestContext.Current.CancellationToken);

        Assert.Single(report.Expiring);
    }
}
