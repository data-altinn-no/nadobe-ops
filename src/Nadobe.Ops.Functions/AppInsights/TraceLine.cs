namespace Nadobe.Ops.Functions.AppInsights;

/// <param name="OperationId">Shared by every line written during one function invocation; empty when unknown.</param>
public sealed record TraceLine(DateTimeOffset Timestamp, string Message, string OperationId);
