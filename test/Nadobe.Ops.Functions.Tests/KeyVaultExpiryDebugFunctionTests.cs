using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class KeyVaultExpiryDebugFunctionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static KeyVaultExpiryDebugFunction CreateFunction(
        IKeyVaultInventory inventory,
        string vaultNames = "vault-a",
        FakeSlackNotifier? notifier = null)
    {
        var scanner = new KeyVaultExpiryScanner(
            inventory,
            Options.Create(new KeyVaultExpiryOptions { KeyVaultNames = vaultNames, KeyVaultExpiryWarningDays = 40 }),
            new FakeTimeProvider(Now),
            NullLogger<KeyVaultExpiryScanner>.Instance);

        return new KeyVaultExpiryDebugFunction(
            scanner,
            notifier ?? new FakeSlackNotifier(),
            NullLogger<KeyVaultExpiryDebugFunction>.Instance);
    }

    private static HttpRequest CreateRequest(string? days = null, string? notify = null)
    {
        var context = new DefaultHttpContext();
        var query = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        if (days is not null)
        {
            query["days"] = days;
        }

        if (notify is not null)
        {
            query["notify"] = notify;
        }

        context.Request.QueryString = QueryString.Create(query);
        return context.Request;
    }

    private static KeyVaultItem Item(string name, double expiresInDays, string vaultName = "vault-a") =>
        new(vaultName, name, KeyVaultItemKind.Secret, Now.AddDays(expiresInDays), Enabled: true);

    [Fact]
    public async Task RunKeyVaultExpiryScan_ReturnsFindings()
    {
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10), Item("later", 100));
        var function = CreateFunction(inventory);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(40, body.WarningDays);
        Assert.Equal(2, body.ItemsScanned);
        Assert.Equal(Now, body.ScannedAtUtc);

        var finding = Assert.Single(body.Expiring);
        Assert.Equal("soon", finding.Name);
        Assert.Equal("Secret", finding.Kind);
        Assert.Equal(10, finding.DaysUntilExpiry);
        Assert.False(finding.Expired);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_DaysQueryOverridesConfiguredWindow()
    {
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("in-90-days", 90));
        var function = CreateFunction(inventory);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest("120"), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        Assert.Equal(120, body.WarningDays);
        Assert.Single(body.Expiring);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("4000")]
    [InlineData("soon")]
    public async Task RunKeyVaultExpiryScan_RejectsInvalidDays(string days)
    {
        var function = CreateFunction(new FakeKeyVaultInventory());

        var result = await function.RunKeyVaultExpiryScan(CreateRequest(days), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_ReportsServerErrorWhenAVaultCannotBeRead()
    {
        var inventory = new FakeKeyVaultInventory()
            .WithFailingVault("vault-a", new InvalidOperationException("forbidden"))
            .WithVault("vault-b", Item("soon", 10, vaultName: "vault-b"));
        var function = CreateFunction(inventory, "vault-a,vault-b");

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Single(body.Failures);
        Assert.Single(body.Expiring);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_ExpiredItemsAreFlaggedWithNegativeDays()
    {
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("stale", -3));
        var function = CreateFunction(inventory);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        var finding = Assert.Single(body.Expiring);
        Assert.True(finding.Expired);
        Assert.Equal(-3, finding.DaysUntilExpiry);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_DoesNotPostToSlackByDefault()
    {
        var notifier = new FakeSlackNotifier();
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier: notifier);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        Assert.Empty(notifier.Posted);
        Assert.Equal("NotRequested", body.SlackStatus);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_PostsToSlackWhenNotifyRequested()
    {
        var notifier = new FakeSlackNotifier();
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier: notifier);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(notify: "true"), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        Assert.Single(notifier.Posted);
        Assert.Equal("Posted", body.SlackStatus);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_ExplainsWhyNothingWasPosted()
    {
        var notifier = new FakeSlackNotifier { Result = SlackPostStatus.NotConfigured };
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier: notifier);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(notify: "true"), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        // Declining to post is not an error, but the response has to say which reason applied.
        Assert.Equal("NotConfigured", body.SlackStatus);
        Assert.Null(body.SlackError);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task RunKeyVaultExpiryScan_ReportsSlackFailureWithoutLosingFindings()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new HttpRequestException("channel_not_found") };
        var inventory = new FakeKeyVaultInventory().WithVault("vault-a", Item("soon", 10));
        var function = CreateFunction(inventory, notifier: notifier);

        var result = Assert.IsType<ObjectResult>(
            await function.RunKeyVaultExpiryScan(CreateRequest(notify: "true"), TestContext.Current.CancellationToken));
        var body = Assert.IsType<KeyVaultExpiryResponse>(result.Value);

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("channel_not_found", body.SlackError);
        Assert.Equal("Failed", body.SlackStatus);
        Assert.Single(body.Expiring);
    }
}
