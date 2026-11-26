using Nadobe.Ops.Functions.AppInsights;
using Nadobe.Ops.Functions.PendingConsentSync;

namespace Nadobe.Ops.Functions.Tests;

public class PendingConsentSyncLogParserTests
{
    private static readonly DateTimeOffset Eleven = new(2026, 11, 26, 11, 0, 0, TimeSpan.Zero);

    // The two lines as Core writes them, see FuncPendingConsentSync and StopwatchLog in the core repo.
    private static TraceLine Summary(
        DateTimeOffset at,
        string operationId,
        int checkedCount = 830,
        int granted = 12,
        int denied = 3,
        int failed = 0,
        int notChecked = 0) =>
        new(
            at,
            $"Completed pending consent sync checked={checkedCount} granted={granted} denied={denied} failed={failed} notChecked={notChecked}",
            operationId);

    private static TraceLine Timer(DateTimeOffset at, string operationId, long elapsedMs) =>
        new(at, $"[NadobeTimer] pending-consent-sync elapsedMs={elapsedMs}", operationId);

    [Fact]
    public void Parse_ReadsTheCountsFromTheSummaryLine()
    {
        var completedAt = Eleven.AddMinutes(4);

        var run = Assert.Single(PendingConsentSyncLogParser.Parse(
            [Summary(completedAt, "op-1", checkedCount: 830, granted: 12, denied: 3, failed: 5, notChecked: 7)]));

        Assert.Equal(completedAt, run.CompletedAt);
        Assert.Equal((830, 12, 3, 5, 7), (run.Checked, run.Granted, run.Denied, run.Failed, run.NotChecked));
    }

    [Fact]
    public void Parse_TakesTheDurationFromTheTimerLineOfTheSameInvocation()
    {
        var runs = PendingConsentSyncLogParser.Parse(
        [
            Timer(Eleven.AddMinutes(4), "op-1", 252_340),
            Summary(Eleven.AddMinutes(4), "op-1"),
        ]);

        Assert.Equal(TimeSpan.FromMilliseconds(252_340), Assert.Single(runs).Duration);
    }

    [Fact]
    public void Parse_LeavesTheDurationOutWhenTheTimerLineIsMissing()
    {
        var run = Assert.Single(PendingConsentSyncLogParser.Parse([Summary(Eleven, "op-1")]));

        Assert.Null(run.Duration);
    }

    [Fact]
    public void Parse_DoesNotBorrowTheDurationOfAnotherInvocation()
    {
        var runs = PendingConsentSyncLogParser.Parse(
        [
            Timer(Eleven, "op-other", 1_000),
            Summary(Eleven, "op-1"),
        ]);

        Assert.Null(Assert.Single(runs).Duration);
    }

    [Fact]
    public void Parse_DoesNotPairLinesThatHaveNoOperationId()
    {
        var runs = PendingConsentSyncLogParser.Parse(
        [
            Timer(Eleven, string.Empty, 1_000),
            Summary(Eleven, string.Empty),
        ]);

        Assert.Null(Assert.Single(runs).Duration);
    }

    [Fact]
    public void Parse_ReturnsEveryRunOldestFirst()
    {
        var runs = PendingConsentSyncLogParser.Parse(
        [
            Summary(Eleven.AddMinutes(20), "op-2", checkedCount: 2),
            Timer(Eleven.AddMinutes(20), "op-2", 2_000),
            Summary(Eleven.AddMinutes(4), "op-1", checkedCount: 1),
            Timer(Eleven.AddMinutes(4), "op-1", 1_000),
        ]);

        Assert.Equal([1, 2], runs.Select(run => run.Checked));
        Assert.Equal([1_000d, 2_000d], runs.Select(run => run.Duration!.Value.TotalMilliseconds));
    }

    [Fact]
    public void Parse_ReadsASummaryWithoutNotChecked()
    {
        // The first version of the sync logged four counts.
        var line = new TraceLine(Eleven, "Completed pending consent sync checked=4 granted=2 denied=1 failed=1", "op-1");

        var run = Assert.Single(PendingConsentSyncLogParser.Parse([line]));

        Assert.Equal((4, 2, 1, 1, 0), (run.Checked, run.Granted, run.Denied, run.Failed, run.NotChecked));
    }

    [Theory]
    [InlineData("Completed pending consent sync")]
    [InlineData("Completed pending consent sync checked=many granted=2 denied=1 failed=1")]
    [InlineData("Stopped the pending consent sync after 10 failures in a row, notChecked=820")]
    [InlineData("[NadobeTimer] pending-consent-sync elapsedMs=1000")]
    [InlineData("")]
    public void Parse_IgnoresLinesThatAreNotASummary(string message)
    {
        Assert.Empty(PendingConsentSyncLogParser.Parse([new TraceLine(Eleven, message, "op-1")]));
    }
}
