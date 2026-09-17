using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Nadobe.Ops.Functions.Tests;

public class HealthFunctionTests
{
    [Fact]
    public void GetHealth_ReportsHealthyWithCurrentTime()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var sut = new HealthFunction(NullLogger<HealthFunction>.Instance, timeProvider);

        var result = Assert.IsType<OkObjectResult>(sut.GetHealth(new DefaultHttpContext().Request));
        var status = Assert.IsType<HealthStatus>(result.Value);

        Assert.Equal("healthy", status.Status);
        Assert.Equal(now, status.CheckedAtUtc);
    }
}
