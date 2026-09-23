using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class KeyVaultExpiryFunctionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static KeyVaultExpiryFunction CreateFunction(
        IKeyVaultInventory inventory,
        FakeSlackNotifier notifier,
        string vaultNames = "vault-a")
    {
        var scanner = new KeyVaultExpiryScanner(
            inventory,
            Options.Create(new KeyVaultExpiryOptions { KeyVaultNames = vaultNames, KeyVaultExpiryWarningDays = 40 }),
            new FakeTimeProvider(Now),
            NullLogger<KeyVaultExpiryScanner>.Instance);

        return new KeyVaultExpiryFunction(scanner, notifier, NullLogger<KeyVaultExpiryFunction>.Instance);
    }

    private static KeyVaultItem Item(string name, double expiresInDays, string vaultName = "vault-a") =>
        new(vaultName, name, KeyVaultItemKind.Secret, Now.AddDays(expiresInDays), Enabled: true);

    [Fact]
    public async Task ScanKeyVaultsForExpiry_PostsFindingsToSlack()
    {
        var notifier = new FakeSlackNotifier();
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));

        await CreateFunction(inventory, notifier)
            .ScanKeyVaultsForExpiry(new TimerInfo(), TestContext.Current.CancellationToken);

        var report = Assert.Single(notifier.Posted);
        Assert.Single(report.Expiring);
    }

    [Fact]
    public async Task ScanKeyVaultsForExpiry_PostsBeforeFailingOnAnUnreadableVault()
    {
        var notifier = new FakeSlackNotifier();
        var inventory = new FakeKeyVaultInventory()
            .WithFailingVault("vault-a", new InvalidOperationException("forbidden"))
            .WithVault("vault-b", Item("soon", 10, vaultName: "vault-b"));
        var function = CreateFunction(inventory, notifier, "vault-a,vault-b");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ScanKeyVaultsForExpiry(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("vault-a: forbidden", exception.Message);
        Assert.Single(notifier.Posted);
    }

    [Fact]
    public async Task ScanKeyVaultsForExpiry_FailsWhenSlackCannotBeReached()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new HttpRequestException("channel_not_found") };
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ScanKeyVaultsForExpiry(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("Slack notification failed: channel_not_found", exception.Message);
    }

    [Fact]
    public async Task ScanKeyVaultsForExpiry_ReportsBothVaultAndSlackProblems()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new HttpRequestException("channel_not_found") };
        var inventory = new FakeKeyVaultInventory().WithFailingVault("vault-a", new InvalidOperationException("forbidden"));
        var function = CreateFunction(inventory, notifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ScanKeyVaultsForExpiry(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("vault-a: forbidden", exception.Message);
        Assert.Contains("Slack notification failed", exception.Message);
    }

    [Fact]
    public async Task ScanKeyVaultsForExpiry_SucceedsWhenNothingIsExpiring()
    {
        var notifier = new FakeSlackNotifier { Result = SlackPostStatus.NothingToPost };
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("later", 200));

        await CreateFunction(inventory, notifier)
            .ScanKeyVaultsForExpiry(new TimerInfo(), TestContext.Current.CancellationToken);

        Assert.Single(notifier.Posted);
    }

    [Fact]
    public async Task ScanKeyVaultsForExpiry_TreatsASlackTimeoutAsAFailureNotACancellation()
    {
        // HttpClient reports its own timeout as TaskCanceledException even though nothing asked us
        // to stop, so it has to be reported as a Slack failure rather than propagating as-is.
        var notifier = new FakeSlackNotifier { ThrowOnPost = new TaskCanceledException("The request timed out.") };
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ScanKeyVaultsForExpiry(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("Slack notification failed", exception.Message);
    }

    [Fact]
    public async Task ScanKeyVaultsForExpiry_PropagatesGenuineCancellation()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new TaskCanceledException("Cancelled.") };
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => function.ScanKeyVaultsForExpiry(new TimerInfo(), cancellation.Token));
    }
}
