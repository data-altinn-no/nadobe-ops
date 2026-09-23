using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nadobe.Ops.Functions.KeyVault;

namespace Nadobe.Ops.Functions.Slack;

public sealed class SlackWebhookNotifier(
    HttpClient httpClient,
    IOptions<SlackOptions> options,
    ILogger<SlackWebhookNotifier> logger) : ISlackNotifier
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<SlackPostStatus> PostAsync(KeyVaultExpiryReport report, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.IsConfigured)
        {
            logger.LogDebug(
                "Slack posting is disabled; set the {SettingName} app setting to enable it.",
                nameof(SlackOptions.SlackWebhookUrl));
            return SlackPostStatus.NotConfigured;
        }

        var hasFindings = report.Expiring.Count > 0 || report.Failures.Count > 0;

        if (!hasFindings && !settings.SlackPostWhenNoFindings)
        {
            return SlackPostStatus.NothingToPost;
        }

        var message = SlackMessageBuilder.Build(report);

        using var response = await httpClient.PostAsJsonAsync(
            settings.SlackWebhookUrl, message, SerializerOptions, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Slack answers a bad payload with a plain-text reason such as "invalid_payload".
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // The URL is a credential, so it is deliberately absent from the message.
            throw new HttpRequestException(
                $"Slack webhook returned {(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        logger.LogInformation(
            "Posted {FindingCount} expiry finding(s) and {FailureCount} vault failure(s) to Slack.",
            report.Expiring.Count, report.Failures.Count);

        return SlackPostStatus.Posted;
    }
}
