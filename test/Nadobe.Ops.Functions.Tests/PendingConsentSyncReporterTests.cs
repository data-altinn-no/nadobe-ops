using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nadobe.Ops.Functions.AppInsights;
using Nadobe.Ops.Functions.PendingConsentSync;

namespace Nadobe.Ops.Functions.Tests;

public class PendingConsentSyncReporterTests
{
    private const string ProdId = "/subscriptions/1/resourceGroups/rg/providers/microsoft.insights/components/appi-prod";
    private const string TestId = "/subscriptions/2/resourceGroups/rg/providers/microsoft.insights/components/appi-test";

    /// <summary>Half past eleven, when the report for the eleven o'clock run is made.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 11, 30, 0, TimeSpan.Zero);

    private static PendingConsentSyncReporter CreateReporter(ITraceLogReader reader, params LogSource[] sources) =>
        new(
            reader,
            Options.Create(new PendingConsentSyncReportOptions { PendingConsentSyncLogSources = [.. sources] }),
            new FakeTimeProvider(Now),
            NullLogger<PendingConsentSyncReporter>.Instance);

    private static LogSource Source(string name, string resourceId) => new() { Name = name, ResourceId = resourceId };

    private static TraceLine Summary(DateTimeOffset at, int checkedCount) =>
        new(at, $"Completed pending consent sync checked={checkedCount} granted=0 denied=0 failed=0 notChecked=0", $"op-{checkedCount}");

    [Fact]
    public async Task ReportAsync_ReportsTheRunsOfEachSourceInConfiguredOrder()
    {
        var reader = new FakeTraceLogReader()
            .WithLines(ProdId, Summary(Now.AddMinutes(-26), 830))
            .WithLines(TestId, Summary(Now.AddMinutes(-29), 3));

        var report = await CreateReporter(reader, Source("prod", ProdId), Source("test", TestId))
            .ReportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["prod", "test"], report.Sources.Select(source => source.Source.DisplayName));
        Assert.Equal(830, Assert.Single(report.Sources[0].Runs).Checked);
        Assert.Equal(3, Assert.Single(report.Sources[1].Runs).Checked);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public async Task ReportAsync_AsksForTheSyncLinesOfTheLastFortyFiveMinutes()
    {
        var reader = new FakeTraceLogReader();

        var report = await CreateReporter(reader, Source("prod", ProdId)).ReportAsync(TestContext.Current.CancellationToken);

        var read = Assert.Single(reader.Reads);
        Assert.Equal(ProdId, read.ResourceId);
        Assert.Equal((Now.AddMinutes(-45), Now), (read.From, read.To));
        Assert.Equal(
            ["Completed pending consent sync", "[NadobeTimer] pending-consent-sync"],
            read.MessagePrefixes);
        Assert.Equal((Now.AddMinutes(-45), Now), (report.From, report.To));
    }

    [Fact]
    public async Task ReportAsync_UsesTheLookbackGivenForThisReport()
    {
        var reader = new FakeTraceLogReader();

        await CreateReporter(reader, Source("prod", ProdId)).ReportAsync(300, TestContext.Current.CancellationToken);

        Assert.Equal(Now.AddMinutes(-300), Assert.Single(reader.Reads).From);
    }

    [Fact]
    public async Task ReportAsync_ReportsAMissingRunWhenTheLogsHoldNone()
    {
        var reader = new FakeTraceLogReader().WithLines(ProdId);

        var report = await CreateReporter(reader, Source("prod", ProdId)).ReportAsync(TestContext.Current.CancellationToken);

        var source = Assert.Single(report.Sources);
        Assert.True(source.IsMissing);
        Assert.Null(source.Failure);
        Assert.True(report.HasProblems);
    }

    [Fact]
    public async Task ReportAsync_KeepsReportingTheOtherSourcesWhenOneCannotBeRead()
    {
        var reader = new FakeTraceLogReader()
            .WithFailingSource(ProdId, new InvalidOperationException("forbidden"))
            .WithLines(TestId, Summary(Now.AddMinutes(-29), 3));

        var report = await CreateReporter(reader, Source("prod", ProdId), Source("test", TestId))
            .ReportAsync(TestContext.Current.CancellationToken);

        Assert.Equal("forbidden", report.Sources[0].Failure);
        Assert.False(report.Sources[0].IsMissing);
        Assert.Single(report.Sources[1].Runs);
        Assert.Equal("prod", Assert.Single(report.Unreadable).Source.DisplayName);
    }

    [Fact]
    public async Task ReportAsync_WithNoSourcesConfigured_ReadsNothing()
    {
        var reader = new FakeTraceLogReader();

        var report = await CreateReporter(reader).ReportAsync(TestContext.Current.CancellationToken);

        Assert.Empty(report.Sources);
        Assert.Empty(reader.Reads);
        Assert.False(report.HasProblems);
    }

    [Fact]
    public async Task ReportAsync_StopsWhenTheInvocationIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var reader = new FakeTraceLogReader()
            .WithFailingSource(ProdId, new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateReporter(reader, Source("prod", ProdId)).ReportAsync(cancellation.Token));
    }
}
