using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

namespace Nadobe.Ops.Functions;

/// <summary>
/// Daily sweep of the configured key vaults for secrets and certificates that are close to expiry.
/// </summary>
public class KeyVaultExpiryFunction(
    KeyVaultExpiryScanner scanner,
    ISlackNotifier notifier,
    ILogger<KeyVaultExpiryFunction> logger)
{
    /// <summary>
    /// Weekdays at 08:00 UTC, which is 10:00 in Oslo during summer time and 09:00 in winter. NCRONTAB
    /// fields are second, minute, hour, day, month, day-of-week. The host runs in UTC: WEBSITE_TIME_ZONE
    /// is not supported on Linux Flex Consumption, so the offset has to live in the expression.
    /// </summary>
    private const string WeekdaysAtEightUtc = "0 0 8 * * 1-5";

    [Function(nameof(ScanKeyVaultsForExpiry))]
    public async Task ScanKeyVaultsForExpiry(
        [TimerTrigger(WeekdaysAtEightUtc)] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var report = await scanner.ScanAsync(cancellationToken);

        logger.LogFindings(report);

        logger.LogInformation(
            "Scanned {ItemsScanned} items; {ExpiringCount} nearing or past expiry. Next run {NextRun}.",
            report.ItemsScanned, report.Expiring.Count, timer.ScheduleStatus?.Next);

        var problems = new List<string>();

        if (report.Failures.Count > 0)
        {
            problems.Add(
                $"Failed to scan {report.Failures.Count} vault(s): " +
                string.Join("; ", report.Failures.Select(failure => $"{failure.VaultName}: {failure.Reason}")));
        }

        try
        {
            await notifier.PostAsync(report, cancellationToken);
        }
        // An HttpClient timeout also surfaces as TaskCanceledException, so only treat cancellation
        // as fatal when it is our own token that was cancelled.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Findings are already in the logs, but undelivered notifications are the failure
            // this job exists to prevent, so they have to be visible too.
            logger.LogError(ex, "Failed to post Key Vault expiry findings to Slack.");
            problems.Add($"Slack notification failed: {ex.Message}");
        }

        if (problems.Count > 0)
        {
            // Surface as a failed invocation so a vault we can no longer read, or a channel we can
            // no longer post to, is alertable rather than silently reported as "nothing expiring".
            throw new InvalidOperationException(string.Join(" | ", problems));
        }
    }
}
