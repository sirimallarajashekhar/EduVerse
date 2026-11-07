using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace EduVerse.Api.Health;

/// <summary>Readiness requires a reachable database with latest required schema migration.</summary>
public sealed class MySqlReadinessCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("EduVerse");
        if (string.IsNullOrWhiteSpace(connectionString))
            return HealthCheckResult.Unhealthy("Database connection is not configured.");
        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(
                "SELECT COUNT(*) FROM schema_migrations WHERE version='003_api_operations';", connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1
                ? HealthCheckResult.Healthy("Database accessible and schema ready.")
                : HealthCheckResult.Unhealthy("Required database schema is not applied.");
        }
        catch (Exception exception) when (exception is MySqlException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("Database not ready.");
        }
    }
}
