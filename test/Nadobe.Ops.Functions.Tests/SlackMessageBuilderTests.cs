using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class SlackMessageBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

    private static KeyVaultExpiryReport Report(
        IReadOnlyList<ExpiringKeyVaultItem>? expiring = null,
        IReadOnlyList<KeyVaultScanFailure>? failures = null,
        int itemsScanned = 10) =>
        new(expiring ?? [], failures ?? [], itemsScanned, 40, Now);

    private static ExpiringKeyVaultItem Entry(
        string name,
        double daysUntilExpiry,
        KeyVaultItemKind kind = KeyVaultItemKind.Secret,
        string vaultName = "vault-a") =>
        new(new KeyVaultItem(vaultName, name, kind, Now.AddDays(daysUntilExpiry), Enabled: true), daysUntilExpiry);

    private static string AllText(SlackMessage message) =>
        string.Join("\n", message.Blocks.Select(block => block.Text.Text));

    [Fact]
    public void Build_SummarisesFindingsInHeaderAndFallbackText()
    {
        var message = SlackMessageBuilder.Build(Report([Entry("a", 10), Entry("b", -2)]));

        var header = message.Blocks[0];
        Assert.Equal("header", header.Type);
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

        Assert.Contains(":red_circle: *stale* (Secret) in `vault-a` — expired 3 day(s) ago", text);
        Assert.Contains(":large_yellow_circle: *soon* (Certificate) in `vault-a` — expires in 12 day(s)", text);
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

        Assert.All(message.Blocks, block => Assert.InRange(block.Text.Text.Length, 1, 3000));
    }

    [Fact]
    public void Build_EscapesSlackControlCharacters()
    {
        var message = SlackMessageBuilder.Build(Report(failures: [new KeyVaultScanFailure("vault-b", "a <b> & c")]));

        Assert.Contains("a &lt;b&gt; &amp; c", AllText(message));
    }
}
