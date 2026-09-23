using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions.Tests;

public class SlackWebhookNotifierTests
{
    private const string WebhookUrl = "https://hooks.slack.example/services/T000/B000/xxx";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);

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

    private static KeyVaultExpiryReport ReportWithFinding() =>
        new(
            [new ExpiringKeyVaultItem(new KeyVaultItem("vault-a", "soon", KeyVaultItemKind.Secret, Now.AddDays(10), true), 10)],
            [],
            1,
            40,
            Now);

    private static KeyVaultExpiryReport EmptyReport() => new([], [], 5, 40, Now);

    private static (SlackWebhookNotifier Notifier, CapturingHandler Handler) CreateNotifier(
        string webhookUrl = WebhookUrl,
        bool postWhenClean = false,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string responseBody = "ok")
    {
        var handler = new CapturingHandler(statusCode, responseBody);
        var notifier = new SlackWebhookNotifier(
            new HttpClient(handler),
            Options.Create(new SlackOptions { SlackWebhookUrl = webhookUrl, SlackPostWhenNoFindings = postWhenClean }),
            NullLogger<SlackWebhookNotifier>.Instance);

        return (notifier, handler);
    }

    [Fact]
    public async Task PostAsync_PostsBlockPayloadToTheConfiguredWebhook()
    {
        var (notifier, handler) = CreateNotifier();

        var posted = await notifier.PostAsync(ReportWithFinding(), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.Posted, posted);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri(WebhookUrl), request.RequestUri);

        using var payload = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Contains("Key Vault expiry", payload.RootElement.GetProperty("text").GetString());
        Assert.NotEmpty(payload.RootElement.GetProperty("blocks").EnumerateArray());
    }

    [Fact]
    public async Task PostAsync_WithoutWebhookUrl_DoesNothing()
    {
        var (notifier, handler) = CreateNotifier(webhookUrl: "");

        var posted = await notifier.PostAsync(ReportWithFinding(), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.NotConfigured, posted);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PostAsync_WithNoFindings_StaysQuietByDefault()
    {
        var (notifier, handler) = CreateNotifier();

        var posted = await notifier.PostAsync(EmptyReport(), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.NothingToPost, posted);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PostAsync_WithNoFindings_PostsWhenHeartbeatEnabled()
    {
        var (notifier, handler) = CreateNotifier(postWhenClean: true);

        var posted = await notifier.PostAsync(EmptyReport(), TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.Posted, posted);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PostAsync_PostsVaultFailuresEvenWithNoExpiringItems()
    {
        var (notifier, handler) = CreateNotifier();
        var report = new KeyVaultExpiryReport([], [new KeyVaultScanFailure("vault-b", "forbidden")], 0, 40, Now);

        var posted = await notifier.PostAsync(report, TestContext.Current.CancellationToken);

        Assert.Equal(SlackPostStatus.Posted, posted);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PostAsync_ThrowsOnSlackErrorWithoutLeakingTheWebhookUrl()
    {
        var (notifier, _) = CreateNotifier(statusCode: HttpStatusCode.NotFound, responseBody: "channel_not_found");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => notifier.PostAsync(ReportWithFinding(), TestContext.Current.CancellationToken));

        Assert.Contains("channel_not_found", exception.Message);
        Assert.DoesNotContain(WebhookUrl, exception.Message);
    }
}
