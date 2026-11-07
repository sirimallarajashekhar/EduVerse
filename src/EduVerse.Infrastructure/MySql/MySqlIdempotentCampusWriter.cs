using System.Security.Cryptography;
using System.Text;
using EduVerse.Application.Operations;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>Serializes per-tenant/actor/operation/key with InnoDB uniqueness and row locking.</summary>
public sealed class MySqlIdempotentCampusWriter(string connectionString) : IIdempotentCampusWriter
{
    public async Task<Campus> CreateAsync(TenantIdentity identity, string code, string name,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code + "\n" + name));
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var ensure = new MySqlCommand("""
            INSERT INTO api_idempotency(tenant_id,subject_id,operation_code,idempotency_key,payload_sha256)
            VALUES(@tid,@subject,'campus.create',@key,@hash)
            ON DUPLICATE KEY UPDATE idempotency_key=idempotency_key
            """, connection, transaction))
        {
            ensure.Parameters.AddWithValue("@tid", identity.TenantId.ToString("D"));
            ensure.Parameters.AddWithValue("@subject", identity.SubjectId);
            ensure.Parameters.AddWithValue("@key", idempotencyKey);
            ensure.Parameters.AddWithValue("@hash", hash);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        byte[] savedHash;
        Guid? existingId;
        await using (var read = new MySqlCommand("""
            SELECT payload_sha256,result_entity_id FROM api_idempotency
            WHERE tenant_id=@tid AND subject_id=@subject AND operation_code='campus.create'
              AND idempotency_key=@key FOR UPDATE
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("@tid", identity.TenantId.ToString("D"));
            read.Parameters.AddWithValue("@subject", identity.SubjectId);
            read.Parameters.AddWithValue("@key", idempotencyKey);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Idempotency row was not reserved.");
            savedHash = (byte[])reader.GetValue(0);
            existingId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }
        if (!CryptographicOperations.FixedTimeEquals(hash, savedHash))
            throw new IdempotencyConflictException();

        if (existingId is Guid id)
        {
            await using var replay = new MySqlCommand("""
                SELECT id,tenant_id,code,display_name,is_active FROM campuses
                WHERE id=@id AND tenant_id=@tid
                """, connection, transaction);
            replay.Parameters.AddWithValue("@id", id.ToString("D"));
            replay.Parameters.AddWithValue("@tid", identity.TenantId.ToString("D"));
            await using var reader = await replay.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Referenced campus no longer exists.");
            var campus = new Campus(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetString(2), reader.GetString(3), reader.GetBoolean(4));
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return campus;
        }

        var newId = Guid.NewGuid();
        await using (var add = new MySqlCommand("""
            INSERT INTO campuses(id,tenant_id,code,display_name,is_active)
            VALUES(@id,@tid,@code,@name,1)
            """, connection, transaction))
        {
            add.Parameters.AddWithValue("@id", newId.ToString("D"));
            add.Parameters.AddWithValue("@tid", identity.TenantId.ToString("D"));
            add.Parameters.AddWithValue("@code", code);
            add.Parameters.AddWithValue("@name", name);
            await add.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var complete = new MySqlCommand("""
            UPDATE api_idempotency SET result_entity_id=@id
            WHERE tenant_id=@tid AND subject_id=@subject AND operation_code='campus.create'
              AND idempotency_key=@key
            """, connection, transaction))
        {
            complete.Parameters.AddWithValue("@id", newId.ToString("D"));
            complete.Parameters.AddWithValue("@tid", identity.TenantId.ToString("D"));
            complete.Parameters.AddWithValue("@subject", identity.SubjectId);
            complete.Parameters.AddWithValue("@key", idempotencyKey);
            await complete.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var outbox = new MySqlCommand("""
            INSERT INTO integration_outbox(id,tenant_id,type,payload_json)
            VALUES (@id,@tid,'campus.created',@payload)
            """, connection, transaction))
        {
            outbox.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("D"));
            outbox.Parameters.AddWithValue("@tid", identity.TenantId.ToString("D"));
            outbox.Parameters.AddWithValue("@payload",
                System.Text.Json.JsonSerializer.Serialize(new { campusId = newId, code }));
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new Campus(newId, identity.TenantId, code, name, true);
    }
}
