namespace Nadobe.Ops.Functions.AppInsights;

/// <summary>
/// An Application Insights instance to read logs from. A class with setters rather than a record,
/// because a list of these is bound from app settings.
/// </summary>
public sealed class LogSource
{
    /// <summary>Short label for messages, such as "prod".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Azure resource id of the Application Insights instance:
    /// <c>/subscriptions/…/resourceGroups/…/providers/microsoft.insights/components/…</c>.
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>What messages call the source: the name, or the name of the resource when none is configured.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : ResourceId.Trim().TrimEnd('/').Split('/')[^1];
}
