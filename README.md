# nadobe-ops

Function app for ops tasks.

## Layout

| Path | Description |
| --- | --- |
| `src/Nadobe.Ops.Functions` | Azure Functions app (.NET 10, isolated worker, x64) |
| `test/Nadobe.Ops.Functions.Tests` | Unit tests (xUnit v3, Microsoft.Testing.Platform) |

## Prerequisites

- .NET SDK 10.0.200 or later (pinned in `global.json`)
- Azure Functions Core Tools v4
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) for local `AzureWebJobsStorage`

## Build, test, run

```sh
dotnet build
dotnet test
cd src/Nadobe.Ops.Functions && func start
```

The sample `GetHealth` function is then available at `http://localhost:7071/api/health`.

`local.settings.json` is git-ignored. On first clone, copy `local.settings.sample.json` to
`local.settings.json` in the same folder.

## Key Vault expiry scan

`ScanKeyVaultsForExpiry` runs weekdays at 07:00 UTC (NCRONTAB `0 0 7 * * 1-5`), which is 09:00 in
Oslo in summer and 08:00 in winter, and logs every secret and certificate expiring within the warning
window. The hour is fixed in UTC because the hosting plan does not support time-zone settings. Items already past expiry are logged at `Error`,
the rest at `Warning`. If a vault cannot be read the invocation fails, so a lost permission is
alertable rather than silently reported as "nothing expiring".

### App settings

| Setting | Required | Default | Description |
| --- | --- | --- | --- |
| `KeyVaultNames` | yes | — | Comma-separated vault names (`vault-a,vault-b`) or full vault URIs. Append `=alias` to label a vault in Slack (`vault-a=dev,vault-b=prod`) |
| `KeyVaultExpiryWarningDays` | no | `40` | Report items expiring within this many days |
| `KeyVaultDnsSuffix` | no | `vault.azure.net` | Override for sovereign clouds |
| `WEBSITE_TIME_ZONE` | — | — | Do **not** set on this app: unsupported on Linux Flex Consumption and can break TLS and metrics. The schedule is UTC |

### Access

The function app's managed identity needs list access to each vault: **Key Vault Reader** under RBAC,
or `list` on secrets and certificates under access policies. A vault uses one model or the other, so
check which before granting.
Only metadata is read — no secret or certificate values are fetched.

### Triggering the scan manually

`RunKeyVaultExpiryScan` runs the same scan on demand and returns the findings in the body:

```sh
# locally, no key needed
curl "http://localhost:7071/api/debug/keyvault-expiry"

# widen the window for this call only
curl "http://localhost:7071/api/debug/keyvault-expiry?days=90"

# deployed, function key required
curl "https://<app>.azurewebsites.net/api/debug/keyvault-expiry?code=<function-key>"
```

`days` is optional (0–3650) and overrides `KeyVaultExpiryWarningDays` for that call only. The
response is `500` when any vault could not be read — the body still lists the findings from the
vaults that succeeded, plus the failing vault and its reason. Findings are also written to the
same log lines as the scheduled run.

The body also carries `slackStatus`, one of `Posted`, `NotRequested`, `NotConfigured`,
`NothingToPost` or `Failed`, so a run that did not post says why. `slackError` holds the webhook's
reason when the status is `Failed`.

## Pending consent sync report

`ReportPendingConsentSyncOnWorkdays` and `ReportPendingConsentSyncOnWeekends` post every run of the
pending consent sync in [core](https://github.com/data-altinn-no/core) to Slack. They do not call
Core. They read the two lines the sync logs at the end of a run from Application Insights:

```
[NadobeTimer] pending-consent-sync elapsedMs=252340
Completed pending consent sync checked=830 granted=12 denied=3 failed=0 notChecked=0
```

The report runs half an hour after the sync starts, so the run has time to finish and its log lines
to arrive: Monday to Friday at 07:30, 11:30, 15:30 and 19:30 UTC, and Saturday and Sunday at 11:30
UTC. The schedules are constants in `PendingConsentSyncReportFunction` and have to follow the ones in
`FuncPendingConsentSync` in Core if those change.

For each log source the report lists the runs that completed in the last 45 minutes, with the counts
and the run time, and flags:

- a run with failures,
- a run that was stopped after too many failures in a row (`notChecked` above 0),
- no completed run at all: the timer did not fire, or the run did not finish,
- logs that could not be read.

Runs and missing runs are also logged, at `Information`, `Warning` or `Error`. The invocation fails
when logs could not be read or the Slack post failed, so a lost permission is alertable. It does not
fail on a missing or failed run of the sync: that is a problem in Core, and reporting it is the job.

### App settings

The log sources are a list, so that more Application Insights instances can be added without a code
change. Each source is a pair of indexed settings:

| Setting | Required | Default | Description |
| --- | --- | --- | --- |
| `PendingConsentSyncLogSources__0__ResourceId` | yes | — | Resource id of the Application Insights instance Core logs to: `/subscriptions/…/resourceGroups/…/providers/microsoft.insights/components/…` |
| `PendingConsentSyncLogSources__0__Name` | no | the resource name | Label for the source in Slack, e.g. `prod` |
| `PendingConsentSyncReportLookbackMinutes` | no | `45` | How far back from the time of the report to look for completed runs |

To report on another environment, add the same two settings with the next index
(`PendingConsentSyncLogSources__1__ResourceId`, `PendingConsentSyncLogSources__1__Name`). Sources are
reported in index order. An entry without a resource id is skipped. With no sources configured the
functions log a warning and post nothing.

The full resource id is needed because the name of an Application Insights instance, unlike a vault
name, is not unique across Azure and gives no address to query.

### Access

The function app's managed identity needs to read the logs of each Application Insights instance:
**Monitoring Reader** on the resource. If the Log Analytics workspace behind it is set to require
workspace permissions, the role has to be granted on the workspace instead.

### Triggering the report manually

`RunPendingConsentSyncReport` makes the same report on demand and returns it in the body:

```sh
# the last 45 minutes
curl "http://localhost:7071/api/debug/pending-consent-sync-report"

# look further back for this call only, here 24 hours
curl "http://localhost:7071/api/debug/pending-consent-sync-report?minutes=1440"

# deployed, function key required; notify=true also posts to Slack
curl "https://<app>.azurewebsites.net/api/debug/pending-consent-sync-report?minutes=1440&notify=true&code=<function-key>"
```

`minutes` is optional (1–10080). The response is `500` when the logs of a source could not be read or
the Slack post failed; the body still lists the other sources, plus the failing one and its reason.
`slackStatus` and `slackError` are as for the Key Vault scan.

### Limits

- A run is only seen through its summary line. If Core's telemetry is sampled and the line is
  dropped, the run is reported as missing.
- A run that takes more than half an hour is reported as missing by the report that follows it.

## Slack notifications

Findings are posted to a Slack [incoming webhook](https://api.slack.com/messaging/webhooks) as a
Block Kit message: a summary header, then one section per vault in configured order, labelled with
the vault's alias and listing certificates and secrets separately (:red_circle: for already expired,
:large_yellow_circle: for approaching), with a divider between vaults. Any vaults that could not be
read and a scan-context footer follow. Lists longer than 25 items are truncated in Slack — the full
set is always in the logs.

| Setting | Required | Default | Description |
| --- | --- | --- | --- |
| `SlackWebhookUrl` | no | — | Incoming-webhook URL. Empty disables posting entirely |
| `SlackPostWhenNoFindings` | no | `false` | Also post an "all clear" when nothing is expiring and every vault was readable, so a silent channel means a broken job |
| `SlackMentionOnFindings` | no | — | Mention to add when items are expiring, or when the pending consent sync needs attention, e.g. `<!subteam^S0123ABCDEF>` for a user group or `<!here>`. Plain `@name` text does not notify |

The pending consent sync report uses the same webhook. It is posted for every run, also the ones that
went well, so `SlackPostWhenNoFindings` does not apply to it. Each run is one line per log source,
marked :white_check_mark: when everything was checked without failures, :warning: when some failed
and :red_circle: when the run was stopped or no run was found.

`SlackWebhookUrl` is a credential — anyone holding it can post to the channel. Store it as a
[Key Vault reference](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
rather than a literal app setting, and keep it out of source. It is never logged, and never included
in error messages.

The scheduled run posts automatically. A failed post fails the invocation, because findings that were
never delivered are the outcome this job exists to prevent. The manual endpoint stays quiet unless
`?notify=true` is passed, so debugging does not wake the channel.
