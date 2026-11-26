using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nadobe.Ops.Functions.AppInsights;
using Nadobe.Ops.Functions.PendingConsentSync;

namespace Nadobe.Ops.Functions.Tests;

public class PendingConsentSyncReportFunctionTests
{
    private const string ProdId = "/subscriptions/1/resourceGroups/rg/providers/microsoft.insights/components/appi-prod";
    private const string TestId = "/subscriptions/2/resourceGroups/rg/providers/microsoft.insights/components/appi-test";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 11, 30, 0, TimeSpan.Zero);

    private static PendingConsentSyncReportFunction CreateFunction(ITraceLogReader reader, FakeSlackNotifier notifier)
    {
        var reporter = new PendingConsentSyncReporter(
            reader,
            Options.Create(new PendingConsentSyncReportOptions
            {
                PendingConsentSyncLogSources =
                [
                    new LogSource { Name = "prod", ResourceId = ProdId },
                    new LogSource { Name = "test", ResourceId = TestId },
                ],
            }),
            new FakeTimeProvider(Now),
            NullLogger<PendingConsentSyncReporter>.Instance);

        return new PendingConsentSyncReportFunction(reporter, notifier, NullLogger<PendingConsentSyncReportFunction>.Instance);
    }

    private static TraceLine Summary(int failed = 0) =>
        new(Now.AddMinutes(-26), $"Completed pending consent sync checked=830 granted=12 denied=3 failed={failed} notChecked=0", "op-1");

    private static FakeTraceLogReader BothSourcesRan() =>
        new FakeTraceLogReader().WithLines(ProdId, Summary()).WithLines(TestId, Summary());

    [Fact]
    public async Task ReportPendingConsentSyncOnWorkdays_PostsTheReportToSlack()
    {
        var notifier = new FakeSlackNotifier();

        await CreateFunction(BothSourcesRan(), notifier)
            .ReportPendingConsentSyncOnWorkdays(new TimerInfo(), TestContext.Current.CancellationToken);

        var report = Assert.Single(notifier.PostedSyncReports);
        Assert.Equal(2, report.Sources.Count);
        Assert.Equal(830, Assert.Single(report.Sources[0].Runs).Checked);
    }

    [Fact]
    public async Task ReportPendingConsentSyncOnWeekends_PostsTheReportToSlack()
    {
        var notifier = new FakeSlackNotifier();

        await CreateFunction(BothSourcesRan(), notifier)
            .ReportPendingConsentSyncOnWeekends(new TimerInfo(), TestContext.Current.CancellationToken);

        Assert.Single(notifier.PostedSyncReports);
    }

    [Fact]
    public async Task Report_PostsAndCompletesWhenARunIsMissingOrHadFailures()
    {
        // These are problems in Core, which the Slack message reports. The report itself did its job.
        var notifier = new FakeSlackNotifier();
        var reader = new FakeTraceLogReader().WithLines(ProdId, Summary(failed: 5)).WithLines(TestId);

        await CreateFunction(reader, notifier)
            .ReportPendingConsentSyncOnWorkdays(new TimerInfo(), TestContext.Current.CancellationToken);

        var report = Assert.Single(notifier.PostedSyncReports);
        Assert.True(report.Sources[1].IsMissing);
    }

    [Fact]
    public async Task Report_PostsBeforeFailingOnUnreadableLogs()
    {
        var notifier = new FakeSlackNotifier();
        var reader = new FakeTraceLogReader()
            .WithFailingSource(ProdId, new InvalidOperationException("forbidden"))
            .WithLines(TestId, Summary());
        var function = CreateFunction(reader, notifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ReportPendingConsentSyncOnWorkdays(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("prod: forbidden", exception.Message);
        Assert.Single(notifier.PostedSyncReports);
    }

    [Fact]
    public async Task Report_FailsWhenSlackCannotBeReached()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new HttpRequestException("channel_not_found") };
        var function = CreateFunction(BothSourcesRan(), notifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ReportPendingConsentSyncOnWorkdays(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("Slack notification failed: channel_not_found", exception.Message);
    }

    [Fact]
    public async Task Report_ReportsBothUnreadableLogsAndASlackFailure()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new HttpRequestException("channel_not_found") };
        var reader = new FakeTraceLogReader().WithFailingSource(ProdId, new InvalidOperationException("forbidden"));
        var function = CreateFunction(reader, notifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => function.ReportPendingConsentSyncOnWorkdays(new TimerInfo(), TestContext.Current.CancellationToken));

        Assert.Contains("prod: forbidden", exception.Message);
        Assert.Contains("Slack notification failed: channel_not_found", exception.Message);
    }
}
