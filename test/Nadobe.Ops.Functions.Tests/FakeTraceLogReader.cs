using Nadobe.Ops.Functions.AppInsights;

namespace Nadobe.Ops.Functions.Tests;

internal sealed class FakeTraceLogReader : ITraceLogReader
{
    private readonly Dictionary<string, IReadOnlyList<TraceLine>> lines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> failures = new(StringComparer.OrdinalIgnoreCase);

    public List<Read> Reads { get; } = [];

    public FakeTraceLogReader WithLines(string resourceId, params TraceLine[] sourceLines)
    {
        lines[resourceId] = sourceLines;
        return this;
    }

    public FakeTraceLogReader WithFailingSource(string resourceId, Exception exception)
    {
        failures[resourceId] = exception;
        return this;
    }

    public Task<IReadOnlyList<TraceLine>> ReadAsync(
        LogSource source,
        IReadOnlyList<string> messagePrefixes,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        Reads.Add(new Read(source.ResourceId, messagePrefixes, from, to));

        if (failures.TryGetValue(source.ResourceId, out var exception))
        {
            throw exception;
        }

        return Task.FromResult(lines.GetValueOrDefault(source.ResourceId, []));
    }

    internal sealed record Read(string ResourceId, IReadOnlyList<string> MessagePrefixes, DateTimeOffset From, DateTimeOffset To);
}
