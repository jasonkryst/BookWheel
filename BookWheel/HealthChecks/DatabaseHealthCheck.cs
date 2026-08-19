using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookWheel.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbContextFactory<BookWheelDbContext> _contextFactory;

    public DatabaseHealthCheck(IDbContextFactory<BookWheelDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var dbContext = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database is reachable.")
                : HealthCheckResult.Unhealthy("Database is not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connectivity check failed.", ex);
        }
    }
}
