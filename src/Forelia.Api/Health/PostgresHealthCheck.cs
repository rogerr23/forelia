using Forelia.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Forelia.Api.Health;

public sealed class PostgresHealthCheck(
    IServiceScopeFactory scopeFactory, ILogger<PostgresHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ForeliaDbContext>();
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Banco de dados indisponível.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao verificar a conexão com PostgreSQL.");
            return HealthCheckResult.Unhealthy("Banco de dados indisponível.");
        }
    }
}
