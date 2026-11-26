using Nadobe.Ops.Functions.AppInsights;

namespace Nadobe.Ops.Functions.Tests;

/// <summary>
/// Covers the query text only. Running it takes an Application Insights instance, so that part is
/// checked through the manual-trigger endpoint.
/// </summary>
public class AzureMonitorTraceLogReaderTests
{
    [Fact]
    public void BuildQuery_KeepsTheLinesStartingWithAnyOfThePrefixes()
    {
        var query = AzureMonitorTraceLogReader.BuildQuery(
            ["Completed pending consent sync", "[NadobeTimer] pending-consent-sync"]);

        Assert.Contains(
            "| where message startswith_cs \"Completed pending consent sync\" or message startswith_cs \"[NadobeTimer] pending-consent-sync\"",
            query);
    }

    [Fact]
    public void BuildQuery_ReadsTheTracesTableOnly()
    {
        // Seen against a real instance: a query scoped to an Application Insights resource resolves
        // "traces" but not the workspace table "AppTraces", and naming both, even in a fuzzy union,
        // comes back as a partial error.
        var query = AzureMonitorTraceLogReader.BuildQuery(["x"]);

        Assert.Equal(
            "traces\n| where message startswith_cs \"x\"\n| project timestamp, message, operation_Id\n| order by timestamp asc",
            query);
    }

    [Fact]
    public void BuildQuery_ReturnsTheColumnsOfATraceLineOldestFirst()
    {
        var query = AzureMonitorTraceLogReader.BuildQuery(["x"]);

        Assert.EndsWith("| project timestamp, message, operation_Id\n| order by timestamp asc", query);
    }

    [Fact]
    public void BuildQuery_EscapesQuotesAndBackslashesInAPrefix()
    {
        var query = AzureMonitorTraceLogReader.BuildQuery(["say \"hi\" \\ bye"]);

        Assert.Contains("message startswith_cs \"say \\\"hi\\\" \\\\ bye\"", query);
    }

    [Fact]
    public void BuildQuery_RefusesToReadEveryTraceLine()
    {
        Assert.Throws<ArgumentException>(() => AzureMonitorTraceLogReader.BuildQuery([]));
    }
}
