using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Nadobe.Ops.Functions;

/// <summary>
/// Minimal HTTP-triggered function used to verify that the host, worker and DI wiring are alive.
/// </summary>
public class HealthFunction(ILogger<HealthFunction> logger, TimeProvider timeProvider)
{
    [Function(nameof(GetHealth))]
    public IActionResult GetHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request)
    {
        logger.LogInformation("Health check requested.");

        return new OkObjectResult(new HealthStatus("healthy", timeProvider.GetUtcNow()));
    }
}

public sealed record HealthStatus(string Status, DateTimeOffset CheckedAtUtc);
