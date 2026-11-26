using Microsoft.Extensions.Configuration;
using Nadobe.Ops.Functions.PendingConsentSync;

namespace Nadobe.Ops.Functions.Tests;

public class PendingConsentSyncReportOptionsTests
{
    private const string ProdId =
        "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-prod/providers/microsoft.insights/components/appi-core-prod";

    private const string TestId =
        "/subscriptions/00000000-0000-0000-0000-000000000002/resourceGroups/rg-test/providers/microsoft.insights/components/appi-core-test";

    /// <summary>
    /// Binds the way Program.cs does, from the configuration root. An app setting named
    /// <c>PendingConsentSyncLogSources__0__Name</c> reaches configuration with colons for the underscores.
    /// </summary>
    private static PendingConsentSyncReportOptions Bind(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        var options = new PendingConsentSyncReportOptions();
        configuration.Bind(options);
        return options;
    }

    [Fact]
    public void GetSources_ReturnsEveryIndexedSourceInOrder()
    {
        var options = Bind(
            ("PendingConsentSyncLogSources:0:Name", "prod"),
            ("PendingConsentSyncLogSources:0:ResourceId", ProdId),
            ("PendingConsentSyncLogSources:1:Name", "test"),
            ("PendingConsentSyncLogSources:1:ResourceId", TestId));

        var sources = options.GetSources();

        Assert.Equal(2, sources.Count);
        Assert.Equal(("prod", ProdId), (sources[0].DisplayName, sources[0].ResourceId));
        Assert.Equal(("test", TestId), (sources[1].DisplayName, sources[1].ResourceId));
    }

    [Fact]
    public void GetSources_IsEmptyWhenNothingIsConfigured()
    {
        Assert.Empty(Bind().GetSources());
    }

    [Fact]
    public void GetSources_NamesASourceAfterItsResourceWhenNoNameIsGiven()
    {
        var options = Bind(("PendingConsentSyncLogSources:0:ResourceId", ProdId));

        Assert.Equal("appi-core-prod", Assert.Single(options.GetSources()).DisplayName);
    }

    [Fact]
    public void GetSources_SkipsEntriesWithoutAResourceId()
    {
        var options = Bind(
            ("PendingConsentSyncLogSources:0:Name", "half-configured"),
            ("PendingConsentSyncLogSources:1:Name", "prod"),
            ("PendingConsentSyncLogSources:1:ResourceId", $"  {ProdId}  "));

        var source = Assert.Single(options.GetSources());

        Assert.Equal("prod", source.DisplayName);
        Assert.Equal(ProdId, source.ResourceId);
    }

    [Fact]
    public void GetSources_KeepsTheFirstEntryForARepeatedResource()
    {
        var options = Bind(
            ("PendingConsentSyncLogSources:0:Name", "prod"),
            ("PendingConsentSyncLogSources:0:ResourceId", ProdId),
            ("PendingConsentSyncLogSources:1:Name", "prod-again"),
            ("PendingConsentSyncLogSources:1:ResourceId", ProdId.ToUpperInvariant()));

        Assert.Equal("prod", Assert.Single(options.GetSources()).DisplayName);
    }

    [Fact]
    public void LookbackMinutes_DefaultsToCoverTheHalfHourBetweenSyncAndReport()
    {
        Assert.Equal(45, Bind().PendingConsentSyncReportLookbackMinutes);
        Assert.Equal(90, Bind(("PendingConsentSyncReportLookbackMinutes", "90")).PendingConsentSyncReportLookbackMinutes);
    }
}
