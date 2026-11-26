using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.PendingConsentSync;

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

        await SendAsync(
            settings, SlackMessageBuilder.Build(report, settings.SlackMentionOnFindings), cancellationToken);

        logger.LogInformation(
            "Posted {FindingCount} expiry finding(s) and {FailureCount} vault failure(s) to Slack.",
            report.Expiring.Count, report.Failures.Count);

        return SlackPostStatus.Posted;
    }

    public async Task<SlackPostStatus> PostAsync(PendingConsentSyncReport report, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        if (!settings.IsConfigured)
        {
            logger.LogDebug(
                "Slack posting is disabled; set the {SettingName} app setting to enable it.",
                nameof(SlackOptions.SlackWebhookUrl));
            return SlackPostStatus.NotConfigured;
        }

        // Every run is reported, the good ones too: SlackPostWhenNoFindings is about the Key Vault
        // all-clear and does not apply. Only a report on no sources at all has nothing to say.
        if (report.Sources.Count == 0)
        {
            return SlackPostStatus.NothingToPost;
        }

        await SendAsync(
            settings, PendingConsentSyncMessageBuilder.Build(report, settings.SlackMentionOnFindings), cancellationToken);

        logger.LogInformation(
            "Posted the pending consent sync report for {SourceCount} log source(s) to Slack.",
            report.Sources.Count);

        return SlackPostStatus.Posted;
    }

    private async Task SendAsync(SlackOptions settings, SlackMessage message, CancellationToken cancellationToken)
    {
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
    }
}
