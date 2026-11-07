using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace EduVerse.Infrastructure.Background;

/// <summary>
/// Single-batch InnoDB SKIP LOCKED worker. Handles only allowlisted internal notifications;
/// extend handlers before enqueueing other event types. At-least-once delivery semantics.
/// </summary>
public sealed class MySqlOutboxWorker(
    IConfiguration configuration, ILogger<MySqlOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!(bool.TryParse(configuration["BackgroundJobs:OutboxEnabled"], out var enabled) && enabled))
        {
            logger.LogInformation("Outbox worker is disabled; configure BackgroundJobs:OutboxEnabled to enable.");
            return;
        }
        var connectionString = configuration.GetConnectionString("EduVerse")
            ?? throw new InvalidOperationException("Outbox worker requires MySQL connection configuration.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(connectionString, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (MySqlException ex) { logger.LogError(ex, "Outbox poll failed."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(string connectionString, CancellationToken ct)
    {
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync(ct);
        for (var i = 0; i < 20; i++)
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            Guid eventId;
            Guid tenantId;
            string eventType;
            await using (var select = new MySqlCommand("""
                SELECT id,tenant_id,type FROM integration_outbox
                WHERE processed_utc IS NULL AND next_attempt_utc<=UTC_TIMESTAMP(6)
                  AND attempts<8 ORDER BY created_utc LIMIT 1 FOR UPDATE SKIP LOCKED
                """, db, tx))
            {
                await using var reader = await select.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    await tx.CommitAsync(ct);
                    return;
                }
                eventId = reader.GetGuid(0);
                tenantId = reader.GetGuid(1);
                eventType = reader.GetString(2);
            }

            // This is an internal proof-of-concept handler, NOT a promise of external delivery.
            // Add an explicit handler per integration before emitting that type in production.
            var workflowEvent = eventType is "workflow.hr.identity_bound" or "workflow.hr.exited"
                or "workflow.procurement.finance_requested";
            var supported = eventType == "campus.created" || workflowEvent;
            if (workflowEvent)
            {
                // Idempotently record the internal receipt in the same transaction.
                // This is not an acknowledgement from any external provider.
                await using var receipt = new MySqlCommand("""
                    INSERT INTO workflow_event_receipts(event_id,tenant_id,event_type)
                    VALUES(@id,@tenant,@type)
                    ON DUPLICATE KEY UPDATE event_id=VALUES(event_id)
                    """, db, tx);
                receipt.Parameters.AddWithValue("@id", eventId.ToString("D"));
                receipt.Parameters.AddWithValue("@tenant", tenantId.ToString("D"));
                receipt.Parameters.AddWithValue("@type", eventType);
                await receipt.ExecuteNonQueryAsync(ct);
            }
            await using var update = new MySqlCommand(supported
                ? """
                  UPDATE integration_outbox SET processed_utc=UTC_TIMESTAMP(6),attempts=attempts+1
                  WHERE id=@id AND tenant_id=@tenant
                  """
                : """
                  UPDATE integration_outbox
                  SET attempts=attempts+1,next_attempt_utc=UTC_TIMESTAMP(6)+INTERVAL 10 MINUTE,
                    last_error='Unsupported outbox event type'
                  WHERE id=@id AND tenant_id=@tenant
                  """, db, tx);
            update.Parameters.AddWithValue("@id", eventId.ToString("D"));
            update.Parameters.AddWithValue("@tenant", tenantId.ToString("D"));
            await update.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
            if (supported)
                logger.LogInformation("Internal outbox event processed; type={EventType}", eventType);
            else
                logger.LogWarning("Outbox event type is not supported; type={EventType}", eventType);
        }
    }
}
