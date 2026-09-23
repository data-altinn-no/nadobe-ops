using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class SlackMessageBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static KeyVaultExpiryReport Report(
        IReadOnlyList<ExpiringKeyVaultItem>? expiring = null,
        IReadOnlyList<KeyVaultScanFailure>? failures = null,
        int itemsScanned = 10,
        IReadOnlyList<KeyVaultTarget>? vaults = null) =>
        new(expiring ?? [], failures ?? [], itemsScanned, 40, Now, vaults);

    private static ExpiringKeyVaultItem Entry(
        string name,
        double daysUntilExpiry,
        KeyVaultItemKind kind = KeyVaultItemKind.Secret,
        string vaultName = "vault-a",
        string? alias = null) =>
        new(new KeyVaultItem(vaultName, name, kind, Now.AddDays(daysUntilExpiry), Enabled: true), daysUntilExpiry, alias);

    private static string AllText(SlackMessage message) =>
        string.Join("\n", message.Blocks.Where(block => block.Text is not null).Select(block => block.Text!.Text));

    [Fact]
    public void Build_SummarisesFindingsInHeaderAndFallbackText()
    {
        var message = SlackMessageBuilder.Build(Report([Entry("a", 10), Entry("b", -2)]));

        var header = message.Blocks[0];
        Assert.Equal("header", header.Type);
        Assert.NotNull(header.Text);
        Assert.Equal("plain_text", header.Text.Type);
        Assert.Equal("Key Vault expiry: 1 already expired, 1 expiring within 40 days", header.Text.Text);
        Assert.Equal(header.Text.Text, message.Text);
    }

    [Fact]
    public void Build_WithOnlyExpiredItems_DoesNotCallThemExpiring()
    {
        var message = SlackMessageBuilder.Build(Report([Entry("a", -40), Entry("b", -1)]));

        Assert.Equal("Key Vault expiry: 2 already expired", message.Text);
    }

    [Fact]
    public void Build_CountsUnreadableVaultsAlongsideFindings()
    {
        var message = SlackMessageBuilder.Build(
            Report([Entry("a", 10)], failures: [new KeyVaultScanFailure("vault-b", "forbidden")]));

        Assert.Equal("Key Vault expiry: 1 expiring within 40 days, 1 vault(s) unreadable", message.Text);
    }

    [Fact]
    public void Build_WithNoFindings_SaysSo()
    {
        var message = SlackMessageBuilder.Build(Report());

        Assert.Equal("Key Vault expiry: nothing expiring", message.Text);
    }

    [Fact]
    public void Build_DistinguishesExpiredFromExpiring()
    {
        var message = SlackMessageBuilder.Build(Report([Entry("stale", -3), Entry("soon", 12, KeyVaultItemKind.Certificate)]));
        var text = AllText(message);

        Assert.Contains(":red_circle: *stale* — expired 3 day(s) ago", text);
        Assert.Contains(":large_yellow_circle: *soon* — expires in 12 day(s)", text);
    }

    [Fact]
    public void Build_GroupsFindingsByVaultThenKind()
    {
        var message = SlackMessageBuilder.Build(Report(
            [
                Entry("old-secret", -3, vaultName: "kv-dev", alias: "dev"),
                Entry("old-cert", 5, KeyVaultItemKind.Certificate, "kv-dev", "dev"),
                Entry("qa-cert", 7, KeyVaultItemKind.Certificate, "kv-qa", "qa"),
            ],
            vaults: [new KeyVaultTarget("kv-dev", "dev"), new KeyVaultTarget("kv-qa", "qa")]));
        var text = AllText(message);

        Assert.Contains("*dev*\n_Certificates_\n:large_yellow_circle: *old-cert*", text);
        Assert.Contains("_Secrets_\n:red_circle: *old-secret*", text);
        Assert.Contains("*qa*\n_Certificates_\n:large_yellow_circle: *qa-cert*", text);
        Assert.DoesNotContain("kv-dev", text);
        Assert.Equal(1, message.Blocks.Count(block => block.Type == "divider"));
    }

    [Fact]
    public void Build_OrdersVaultGroupsAsConfigured()
    {
        // The most urgent finding is in kv-dev, but kv-prod is configured first and should lead.
        var message = SlackMessageBuilder.Build(Report(
            [Entry("dev-secret", -30, vaultName: "kv-dev"), Entry("prod-secret", 10, vaultName: "kv-prod")],
            vaults: [new KeyVaultTarget("kv-prod", "prod"), new KeyVaultTarget("kv-dev", "dev")]));
        var text = AllText(message);

        Assert.True(text.IndexOf("*kv-prod*", StringComparison.Ordinal) < text.IndexOf("*kv-dev*", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_IncludesVaultFailures()
    {
        var message = SlackMessageBuilder.Build(Report(failures: [new KeyVaultScanFailure("vault-b", "forbidden")]));
        var text = AllText(message);

        Assert.Contains("Could not read vault `vault-b`: forbidden", text);
        Assert.Contains("1 vault(s) unreadable", message.Text);
    }

    [Fact]
    public void Build_AlwaysIncludesScanContext()
    {
        var message = SlackMessageBuilder.Build(Report(itemsScanned: 42));

        Assert.Contains("Scanned 42 item(s) with a 40-day window", AllText(message));
    }

    [Fact]
    public void Build_TruncatesLongFindingLists()
    {
        var entries = Enumerable.Range(0, SlackMessageBuilder.MaxListedFindings + 7)
            .Select(index => Entry($"secret-{index}", index))
            .ToArray();

        var message = SlackMessageBuilder.Build(Report(entries));
        var text = AllText(message);

        Assert.Contains("…and 7 more", text);
        Assert.DoesNotContain($"secret-{SlackMessageBuilder.MaxListedFindings}*", text);
    }

    [Fact]
    public void Build_KeepsEverySectionWithinSlacksLengthLimit()
    {
        var entries = Enumerable.Range(0, SlackMessageBuilder.MaxListedFindings)
            .Select(index => Entry(new string('x', 200) + index, index))
            .ToArray();

        var message = SlackMessageBuilder.Build(Report(entries));

        Assert.All(
            message.Blocks.Where(block => block.Text is not null),
            block => Assert.InRange(block.Text!.Text.Length, 1, 3000));
    }

    [Fact]
    public void Build_EscapesSlackControlCharacters()
    {
        var message = SlackMessageBuilder.Build(Report(failures: [new KeyVaultScanFailure("vault-b", "a <b> & c")]));

        Assert.Contains("a &lt;b&gt; &amp; c", AllText(message));
    }
}
