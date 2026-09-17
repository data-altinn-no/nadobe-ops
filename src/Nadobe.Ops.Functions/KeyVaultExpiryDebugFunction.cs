using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions;

/// <summary>
/// Runs the same scan as <see cref="KeyVaultExpiryFunction"/> on demand and returns the findings in
/// the response body, so the schedule does not have to be waited out when debugging.
/// </summary>
public class KeyVaultExpiryDebugFunction(
    KeyVaultExpiryScanner scanner,
    ISlackNotifier notifier,
    ILogger<KeyVaultExpiryDebugFunction> logger)
{
    private const int MaxWarningDays = 3650;

    /// <summary>
    /// GET /api/debug/keyvault-expiry?days=90&amp;notify=true — <c>days</c> overrides the configured
    /// window for this call only, and <c>notify</c> opts this run into posting to Slack.
    /// </summary>
    [Function(nameof(RunKeyVaultExpiryScan))]
    public async Task<IActionResult> RunKeyVaultExpiryScan(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "debug/keyvault-expiry")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadWarningDays(request, out var warningDays, out var error))
        {
            return new BadRequestObjectResult(new { error });
        }

        // Debugging should not wake the channel unless that is what is being debugged.
        var notify = request.Query.TryGetValue("notify", out var raw) && bool.TryParse(raw, out var parsed) && parsed;

        logger.LogInformation(
            "Manual Key Vault expiry scan requested (days override: {WarningDays}, notify: {Notify}).",
            warningDays, notify);

        var report = await scanner.ScanAsync(warningDays, cancellationToken);

        logger.LogFindings(report);

        var postedToSlack = false;
        string? slackError = null;

        if (notify)
        {
            try
            {
                postedToSlack = await notifier.PostAsync(report, cancellationToken);
            }
            // An HttpClient timeout also surfaces as TaskCanceledException, so only treat cancellation
            // as fatal when it is our own token that was cancelled.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to post Key Vault expiry findings to Slack.");
                slackError = ex.Message;
            }
        }

        var response = KeyVaultExpiryResponse.From(report, postedToSlack, slackError);

        // Mirror the timer's behaviour: an unreadable vault is a failure, not an empty result.
        // The body is still returned so the failing vault and its reason are visible.
        var failed = report.Failures.Count > 0 || slackError is not null;

        return new ObjectResult(response)
        {
            StatusCode = failed ? StatusCodes.Status500InternalServerError : StatusCodes.Status200OK,
        };
    }

    private static bool TryReadWarningDays(HttpRequest request, out int? warningDays, out string? error)
    {
        warningDays = null;
        error = null;

        if (!request.Query.TryGetValue("days", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, out var parsed) || parsed < 0 || parsed > MaxWarningDays)
        {
            error = $"'days' must be an integer between 0 and {MaxWarningDays}.";
            return false;
        }

        warningDays = parsed;
        return true;
    }
}
