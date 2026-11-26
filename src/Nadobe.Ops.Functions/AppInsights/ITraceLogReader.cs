namespace Nadobe.Ops.Functions.AppInsights;

/// <summary>
/// Reads trace lines from a single log source. Kept deliberately thin so that the reports built on
/// it can be tested without touching Azure.
/// </summary>
public interface ITraceLogReader
{
    /// <summary>
    /// The trace lines logged between <paramref name="from"/> and <paramref name="to"/> whose message
    /// starts with one of <paramref name="messagePrefixes"/>, oldest first.
    /// </summary>
    Task<IReadOnlyList<TraceLine>> ReadAsync(
        LogSource source,
        IReadOnlyList<string> messagePrefixes,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
