using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nadobe.Ops.Functions.AppInsights;
using Nadobe.Ops.Functions.PendingConsentSync;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class SlackWebhookNotifierPendingConsentSyncTests
{
    private const string WebhookUrl = "https://hooks.slack.example/services/T000/B000/xxx";

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 11, 30, 0, TimeSpan.Zero);

    private sealed class CapturingHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        }
    }

    private static PendingConsentSyncReport Report(params PendingConsentSyncSourceReport[] sources) =>
        new(sources, Now.AddMinutes(-45), Now);

    private static PendingConsentSyncSourceReport Prod(params PendingConsentSyncRun[] runs) =>
        new(new LogSource { Name = "prod", ResourceId = "/components/prod" }, runs);

    private static PendingConsentSyncRun CleanRun() => new(Now.AddMinutes(-26), 830, 12, 3, 0, 0, TimeSpan.FromSeconds(252));

    private static (SlackWebhookNotifier Notifier, CapturingHandler Handler) CreateNotifier(
        string webhookUrl = WebhookUrl,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string responseBody = "ok",
        string? mention = null)
    {
        var handler = new CapturingHandler(statusCode, responseBody);
        var notifier = new SlackWebhookNotifier(
            new HttpClient(handler),
            Options.Create(new SlackOptions
            {
                SlackWebhookUrl = webhookUrl,
                // Off, to show that it only governs the Key Vault all-clear.
                SlackPostWhenNoFindings = false,
                SlackMentionOnFindings = mention ?? string.Empty,
            }),
            NullLogger<SlackWebhookNotifier>.Instance);

        return (notifier, handler);
    }

    [Fact]
    public async Task PostAsync_PostsARunThatWentWell()
    {
        var (notifier, handler) = CreateNotifier();

        var posted = await notifier.PostAsync(Report(Prod(CleanRun())), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.Posted, posted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri(WebhookUrl), request.RequestUri);

        using var payload = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Contains("Pending consent sync completed", payload.RootElement.GetProperty("text").GetString());
        Assert.NotEmpty(payload.RootElement.GetProperty("blocks").EnumerateArray());
    }

    [Fact]
    public async Task PostAsync_MentionsWhenARunIsMissing()
    {
        var (notifier, handler) = CreateNotifier(mention: "<!subteam^S0123ABCDEF>");

        await notifier.PostAsync(Report(Prod()), TestContext.Current.CancellationToken);

        using var payload = JsonDocument.Parse(handler.Bodies[0]);
        Assert.StartsWith("<!subteam^S0123ABCDEF> ", payload.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task PostAsync_WithoutWebhookUrl_DoesNothing()
    {
        var (notifier, handler) = CreateNotifier(webhookUrl: "");

        var posted = await notifier.PostAsync(Report(Prod(CleanRun())), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.NotConfigured, posted);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PostAsync_WithNoSourcesInTheReport_HasNothingToPost()
    {
        var (notifier, handler) = CreateNotifier();

        var posted = await notifier.PostAsync(Report(), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.NothingToPost, posted);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PostAsync_ThrowsOnSlackErrorWithoutLeakingTheWebhookUrl()
    {
        var (notifier, _) = CreateNotifier(statusCode: HttpStatusCode.NotFound, responseBody: "channel_not_found");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => notifier.PostAsync(Report(Prod(CleanRun())), TestContext.Current.CancellationToken));

        Assert.Contains("channel_not_found", exception.Message);
        Assert.DoesNotContain(WebhookUrl, exception.Message);
    }
}
