using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Nadobe.Ops.Functions.AppInsights;
using Nadobe.Ops.Functions.PendingConsentSync;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class PendingConsentSyncReportDebugFunctionTests
{
    private const string ProdId = "/subscriptions/1/resourceGroups/rg/providers/microsoft.insights/components/appi-prod";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 11, 30, 0, TimeSpan.Zero);

    private static PendingConsentSyncReportDebugFunction CreateFunction(ITraceLogReader reader, FakeSlackNotifier? notifier = null)
    {
        var reporter = new PendingConsentSyncReporter(
            reader,
            Options.Create(new PendingConsentSyncReportOptions
            {
                PendingConsentSyncLogSources = [new LogSource { Name = "prod", ResourceId = ProdId }],
            }),
            new FakeTimeProvider(Now),
            NullLogger<PendingConsentSyncReporter>.Instance);

        return new PendingConsentSyncReportDebugFunction(
            reporter,
            notifier ?? new FakeSlackNotifier(),
            NullLogger<PendingConsentSyncReportDebugFunction>.Instance);
    }

    private static HttpRequest CreateRequest(string? minutes = null, string? notify = null)
    {
        var context = new DefaultHttpContext();
        var query = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>();

        if (minutes is not null)
        {
            query["minutes"] = minutes;
        }

        if (notify is not null)
        {
            query["notify"] = notify;
        }

        context.Request.QueryString = QueryString.Create(query);
        return context.Request;
    }

    private static FakeTraceLogReader ProdRan() =>
        new FakeTraceLogReader().WithLines(
            ProdId,
            new TraceLine(Now.AddMinutes(-26), "[NadobeTimer] pending-consent-sync elapsedMs=252000", "op-1"),
            new TraceLine(Now.AddMinutes(-26), "Completed pending consent sync checked=830 granted=12 denied=3 failed=0 notChecked=0", "op-1"));

    private static async Task<(ObjectResult Result, PendingConsentSyncReportResponse Body)> Run(
        PendingConsentSyncReportDebugFunction function,
        HttpRequest request)
    {
        var result = Assert.IsType<ObjectResult>(
            await function.RunPendingConsentSyncReport(request, TestContext.Current.CancellationToken));

        return (result, Assert.IsType<PendingConsentSyncReportResponse>(result.Value));
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_ReturnsTheRuns()
    {
        var (result, body) = await Run(CreateFunction(ProdRan()), CreateRequest());

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal((Now.AddMinutes(-45), Now), (body.From, body.To));

        var source = Assert.Single(body.Sources);
        Assert.Equal(("prod", ProdId), (source.Name, source.ResourceId));
        Assert.False(source.MissingRun);
        Assert.Null(source.Failure);

        var run = Assert.Single(source.Runs);
        Assert.Equal((830, 12, 3, 0, 0), (run.Checked, run.Granted, run.Denied, run.Failed, run.NotChecked));
        Assert.Equal(TimeSpan.FromSeconds(252), run.Duration);
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_StaysQuietUnlessAskedToNotify()
    {
        var notifier = new FakeSlackNotifier();

        var (_, body) = await Run(CreateFunction(ProdRan(), notifier), CreateRequest());

        Assert.Equal(nameof(SlackPostStatus.NotRequested), body.SlackStatus);
        Assert.Empty(notifier.PostedSyncReports);
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_PostsWhenAskedToNotify()
    {
        var notifier = new FakeSlackNotifier();

        var (result, body) = await Run(CreateFunction(ProdRan(), notifier), CreateRequest(notify: "true"));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal(nameof(SlackPostStatus.Posted), body.SlackStatus);
        Assert.Single(notifier.PostedSyncReports);
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_LooksBackAsFarAsAsked()
    {
        var reader = ProdRan();

        var (_, body) = await Run(CreateFunction(reader), CreateRequest(minutes: "300"));

        Assert.Equal(Now.AddMinutes(-300), body.From);
        Assert.Equal(Now.AddMinutes(-300), Assert.Single(reader.Reads).From);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    [InlineData("10081")]
    public async Task RunPendingConsentSyncReport_RejectsAnInvalidLookback(string minutes)
    {
        var reader = ProdRan();

        var result = await CreateFunction(reader).RunPendingConsentSyncReport(
            CreateRequest(minutes: minutes), TestContext.Current.CancellationToken);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(reader.Reads);
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_ReportsAMissingRunWithoutFailing()
    {
        var (result, body) = await Run(CreateFunction(new FakeTraceLogReader()), CreateRequest());

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.True(Assert.Single(body.Sources).MissingRun);
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_Returns500WithTheReasonWhenLogsCannotBeRead()
    {
        var reader = new FakeTraceLogReader().WithFailingSource(ProdId, new InvalidOperationException("forbidden"));

        var (result, body) = await Run(CreateFunction(reader), CreateRequest());

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal("forbidden", Assert.Single(body.Sources).Failure);
    }

    [Fact]
    public async Task RunPendingConsentSyncReport_Returns500WithTheReasonWhenSlackFails()
    {
        var notifier = new FakeSlackNotifier { ThrowOnPost = new HttpRequestException("channel_not_found") };

        var (result, body) = await Run(CreateFunction(ProdRan(), notifier), CreateRequest(notify: "true"));

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
        Assert.Equal(nameof(SlackPostStatus.Failed), body.SlackStatus);
        Assert.Equal("channel_not_found", body.SlackError);
    }
}
