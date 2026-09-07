using Microsoft.Extensions.Diagnostics.HealthChecks;
using TrackZ.Application.Common.Interfaces;

namespace TrackZ.Api.Health;

public sealed class ObjectStorageReadinessHealthCheck(IObjectStorage objectStorage) : IHealthCheck
{
    private const string HealthPrefix = "health/";
    private const string HealthKey = HealthPrefix + "readiness";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stored = await objectStorage.GetAsync(HealthPrefix, HealthKey, cancellationToken);
            if (stored is not null) await stored.Content.DisposeAsync();
            return HealthCheckResult.Healthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
