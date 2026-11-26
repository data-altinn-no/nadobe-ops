using Nadobe.Ops.Functions.AppInsights;
using Nadobe.Ops.Functions.PendingConsentSync;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class PendingConsentSyncMessageBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 11, 30, 0, TimeSpan.Zero);

    private static PendingConsentSyncRun Run(
        int checkedCount = 830,
        int granted = 12,
        int denied = 3,
        int failed = 0,
        int notChecked = 0,
        double? seconds = 252) =>
        new(
            Now.AddMinutes(-26),
            checkedCount,
            granted,
            denied,
            failed,
            notChecked,
            seconds is { } value ? TimeSpan.FromSeconds(value) : null);

    private static PendingConsentSyncSourceReport Source(string name, params PendingConsentSyncRun[] runs) =>
        new(new LogSource { Name = name, ResourceId = $"/components/{name}" }, runs);

    private static PendingConsentSyncSourceReport Unreadable(string name, string reason) =>
        new(new LogSource { Name = name, ResourceId = $"/components/{name}" }, [], reason);

    private static PendingConsentSyncReport Report(params PendingConsentSyncSourceReport[] sources) =>
        new(sources, Now.AddMinutes(-45), Now);

    private static string AllText(SlackMessage message) =>
        string.Join("\n", message.Blocks.Where(block => block.Text is not null).Select(block => block.Text!.Text));

    [Fact]
    public void Build_SaysTheSyncCompletedInHeaderAndFallbackText()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run())));

        var header = message.Blocks[0];
        Assert.Equal("header", header.Type);
        Assert.Equal("plain_text", header.Text!.Type);
        Assert.Equal("✅ Pending consent sync completed", header.Text.Text);
        Assert.Equal(header.Text.Text, message.Text);
    }

    [Fact]
    public void Build_ListsTheCountsTimeAndDurationOfARun()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run())));

        Assert.Contains(
            ":white_check_mark: *prod* 11:04 UTC — checked 830, granted 12, denied 3, failed 0 · took 4 min 12 s",
            AllText(message));
    }

    [Theory]
    [InlineData(0.85, "850 ms")]
    [InlineData(12.4, "12 s")]
    [InlineData(60, "1 min 0 s")]
    [InlineData(1000, "16 min 40 s")]
    public void Build_WritesTheDurationInTheLargestFittingUnits(double seconds, string expected)
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run(seconds: seconds))));

        Assert.Contains($"· took {expected}", AllText(message));
    }

    [Fact]
    public void Build_LeavesTheDurationOutWhenItIsUnknown()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run(seconds: null))));

        Assert.DoesNotContain("took", AllText(message));
    }

    [Fact]
    public void Build_FlagsARunWithFailures()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run(failed: 5))));

        Assert.Equal("⚠️ Pending consent sync needs attention", message.Text);
        Assert.Contains(":warning: *prod* 11:04 UTC — checked 830, granted 12, denied 3, *failed 5*", AllText(message));
    }

    [Fact]
    public void Build_FlagsARunThatWasStopped()
    {
        var message = PendingConsentSyncMessageBuilder.Build(
            Report(Source("prod", Run(checkedCount: 10, granted: 0, denied: 0, failed: 10, notChecked: 820))));

        Assert.Equal("⚠️ Pending consent sync needs attention", message.Text);
        Assert.Contains(
            ":red_circle: *prod* 11:04 UTC — stopped after repeated failures, *820 not checked*. Checked 10, granted 0, denied 0, failed 10",
            AllText(message));
    }

    [Fact]
    public void Build_FlagsASourceWithoutACompletedRun()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod")));

        Assert.Equal("⚠️ Pending consent sync needs attention", message.Text);
        Assert.Contains(":red_circle: *prod* — no completed run found in the logs", AllText(message));
    }

    [Fact]
    public void Build_FlagsASourceWhoseLogsCouldNotBeRead()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Unreadable("prod", "403 <Forbidden>")));

        Assert.Equal("⚠️ Pending consent sync needs attention", message.Text);
        Assert.Contains(":warning: Could not read the logs for *prod*: 403 &lt;Forbidden&gt;", AllText(message));
        Assert.DoesNotContain("no completed run", AllText(message));
    }

    [Fact]
    public void Build_ListsEveryRunOfEverySource()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(
            Source("prod", Run(checkedCount: 1), Run(checkedCount: 2)),
            Source("test", Run(checkedCount: 3))));

        var text = AllText(message);
        Assert.Contains("*prod* 11:04 UTC — checked 1,", text);
        Assert.Contains("*prod* 11:04 UTC — checked 2,", text);
        Assert.Contains("*test* 11:04 UTC — checked 3,", text);
        Assert.Equal("✅ Pending consent sync completed", message.Text);
    }

    [Fact]
    public void Build_EscapesSourceNames()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("<prod> & co", Run())));

        Assert.Contains("*&lt;prod&gt; &amp; co*", AllText(message));
    }

    [Fact]
    public void Build_MentionsOnlyWhenSomethingNeedsAttention()
    {
        const string mention = "<!subteam^S0123ABCDEF>";

        var quiet = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run())), mention);
        var loud = PendingConsentSyncMessageBuilder.Build(Report(Source("prod")), mention);

        Assert.DoesNotContain(mention, AllText(quiet));
        Assert.DoesNotContain(mention, quiet.Text);
        Assert.Contains(mention, AllText(loud));
        Assert.StartsWith($"{mention} ", loud.Text);
    }

    [Fact]
    public void Build_EndsWithTheWindowTheLogsWereReadFor()
    {
        var message = PendingConsentSyncMessageBuilder.Build(Report(Source("prod", Run())));

        Assert.Equal(
            "_Runs completed between 2026-11-26 10:45:00Z and 2026-11-26 11:30:00Z._",
            message.Blocks[^1].Text!.Text);
    }
}
