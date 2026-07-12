using DealerOS.Modules.Inspections.Application;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DealerOS.Api.Infrastructure;

public sealed class ObjectStorageHealthCheck(IInspectionPhotoStorage storage) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.EnsureBucketAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Object storage is unavailable.", exception);
        }
    }
}
