using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nadobe.Ops.Functions.PendingConsentSync;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions;

/// <summary>
/// Makes the same report as <see cref="PendingConsentSyncReportFunction"/> on demand and returns it in
/// the response body, so the schedule does not have to be waited out when debugging.
/// </summary>
public class PendingConsentSyncReportDebugFunction(
    PendingConsentSyncReporter reporter,
    ISlackNotifier notifier,
    ILogger<PendingConsentSyncReportDebugFunction> logger)
{
    /// <summary>A week, which is as far back as a manual look has reason to go.</summary>
    private const int MaxLookbackMinutes = 7 * 24 * 60;

    /// <summary>
    /// GET /api/debug/pending-consent-sync-report?minutes=300&amp;notify=true — <c>minutes</c> overrides
    /// how far back to look for this call only, and <c>notify</c> opts this report into posting to Slack.
    /// </summary>
    [Function(nameof(RunPendingConsentSyncReport))]
    public async Task<IActionResult> RunPendingConsentSyncReport(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "debug/pending-consent-sync-report")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryReadLookbackMinutes(request, out var lookbackMinutes, out var error))
        {
            return new BadRequestObjectResult(new { error });
        }

        // Debugging should not wake the channel unless that is what is being debugged.
        var notify = request.Query.TryGetValue("notify", out var raw) && bool.TryParse(raw, out var parsed) && parsed;

        logger.LogInformation(
            "Manual pending consent sync report requested (minutes override: {LookbackMinutes}, notify: {Notify}).",
            lookbackMinutes, notify);

        var report = await reporter.ReportAsync(lookbackMinutes, cancellationToken);

        logger.LogReport(report);

        var slackStatus = SlackPostStatus.NotRequested;
        string? slackError = null;

        if (notify)
        {
            try
            {
                slackStatus = await notifier.PostAsync(report, cancellationToken);
            }
            // An HttpClient timeout also surfaces as TaskCanceledException, so only treat cancellation
            // as fatal when it is our own token that was cancelled.
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to post the pending consent sync report to Slack.");
                slackStatus = SlackPostStatus.Failed;
                slackError = ex.Message;
            }
        }

        var response = PendingConsentSyncReportResponse.Create(report, slackStatus, slackError);

        // Mirror the timer: unreadable logs are a failure, a missing run is a finding. The body is
        // still returned, so the failing source and its reason are visible.
        var failed = report.Unreadable.Count > 0 || slackError is not null;

        return new ObjectResult(response)
        {
            StatusCode = failed ? StatusCodes.Status500InternalServerError : StatusCodes.Status200OK,
        };
    }

    private static bool TryReadLookbackMinutes(HttpRequest request, out int? lookbackMinutes, out string? error)
    {
        lookbackMinutes = null;
        error = null;

        if (!request.Query.TryGetValue("minutes", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, out var parsed) || parsed < 1 || parsed > MaxLookbackMinutes)
        {
            error = $"'minutes' must be an integer between 1 and {MaxLookbackMinutes}.";
            return false;
        }

        lookbackMinutes = parsed;
        return true;
    }
}
