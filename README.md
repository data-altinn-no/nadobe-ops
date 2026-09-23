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

`ScanKeyVaultsForExpiry` runs weekdays at 08:00 UTC (NCRONTAB `0 0 8 * * 1-5`), which is 10:00 in
Oslo in summer and 09:00 in winter, and logs every secret and certificate expiring within the warning
window. The hour is fixed in UTC because the hosting plan does not support time-zone settings. Items already past expiry are logged at `Error`,
the rest at `Warning`. If a vault cannot be read the invocation fails, so a lost permission is
alertable rather than silently reported as "nothing expiring".

### App settings

| Setting | Required | Default | Description |
| --- | --- | --- | --- |
| `KeyVaultNames` | yes | — | Comma-separated vault names (`vault-a,vault-b`) or full vault URIs |
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

## Slack notifications

Findings are posted to a Slack [incoming webhook](https://api.slack.com/messaging/webhooks) as a
Block Kit message: a summary header, one line per item (:red_circle: for already expired,
:large_yellow_circle: for approaching), any vaults that could not be read, and a scan-context footer.
Lists longer than 25 items are truncated in Slack — the full set is always in the logs.

| Setting | Required | Default | Description |
| --- | --- | --- | --- |
| `SlackWebhookUrl` | no | — | Incoming-webhook URL. Empty disables posting entirely |
| `SlackPostWhenNoFindings` | no | `false` | Also post "nothing expiring", so a silent channel means a broken job |

`SlackWebhookUrl` is a credential — anyone holding it can post to the channel. Store it as a
[Key Vault reference](https://learn.microsoft.com/azure/app-service/app-service-key-vault-references)
rather than a literal app setting, and keep it out of source. It is never logged, and never included
in error messages.

The scheduled run posts automatically. A failed post fails the invocation, because findings that were
never delivered are the outcome this job exists to prevent. The manual endpoint stays quiet unless
`?notify=true` is passed, so debugging does not wake the channel.
