using Azure.Core;
using Azure.Monitor.Query.Logs;

namespace Nadobe.Ops.Functions.AppInsights;

/// <summary>Reads trace lines from an Application Insights instance through the Azure Monitor logs API.</summary>
public sealed class AzureMonitorTraceLogReader(TokenCredential credential) : ITraceLogReader
{
    private readonly LogsQueryClient client = new(credential);

    public async Task<IReadOnlyList<TraceLine>> ReadAsync(
        LogSource source,
        IReadOnlyList<string> messagePrefixes,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var response = await client.QueryResourceAsync(
            new ResourceIdentifier(source.ResourceId),
            BuildQuery(messagePrefixes),
            new LogsQueryTimeRange(from, to),
            cancellationToken: cancellationToken);

        var lines = new List<TraceLine>();

        foreach (var row in response.Value.Table.Rows)
        {
            if (row.GetDateTimeOffset("timestamp") is not { } timestamp)
            {
                continue;
            }

            lines.Add(new TraceLine(
                timestamp,
                row.GetString("message") ?? string.Empty,
                row.GetString("operation_Id") ?? string.Empty));
        }

        return lines;
    }

    /// <summary>
    /// The query for the trace lines whose message starts with one of <paramref name="messagePrefixes"/>.
    /// The time window is not part of it; that goes with the request.
    /// </summary>
    public static string BuildQuery(IReadOnlyList<string> messagePrefixes)
    {
        if (messagePrefixes.Count == 0)
        {
            throw new ArgumentException("At least one message prefix is needed.", nameof(messagePrefixes));
        }

        var filter = string.Join(" or ", messagePrefixes.Select(prefix => $"message startswith_cs {Quote(prefix)}"));

        // A query scoped to an Application Insights instance sees its trace lines as "traces". The
        // workspace name for the same table, "AppTraces", does not resolve there.
        return string.Join(
            '\n',
            "traces",
            $"| where {filter}",
            "| project timestamp, message, operation_Id",
            "| order by timestamp asc");
    }

    /// <summary>A KQL string literal.</summary>
    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
