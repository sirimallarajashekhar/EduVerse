using EduVerse.Application.Tenancy;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>Parameterized, tenant-scoped SQL. Connections are pooled by MySqlConnector.</summary>
public sealed class MySqlInstitutionRepository(string connectionString) : IInstitutionRepository
{
    private MySqlConnection OpenConnection() => new(connectionString);

    public async Task<Institution?> GetAccessibleInstitutionAsync(TenantIdentity identity, CancellationToken ct)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            SELECT t.id, t.slug, t.display_name, t.kind, t.is_active
            FROM tenants t
            INNER JOIN tenant_memberships m ON m.tenant_id = t.id
            WHERE t.id = @tenantId AND m.subject_id = @subjectId
              AND t.is_active = 1 AND m.is_active = 1
            LIMIT 1;
            """, connection);
        command.Parameters.AddWithValue("@tenantId", identity.TenantId.ToString("D"));
        command.Parameters.AddWithValue("@subjectId", identity.SubjectId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new Institution(reader.GetGuid(0), reader.GetString(1),
            reader.GetString(2), reader.GetString(3), reader.GetBoolean(4));
    }

    public async Task<IReadOnlyList<Campus>> ListCampusesAsync(Guid tenantId, CancellationToken ct)
    {
        var campuses = new List<Campus>();
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            SELECT id, tenant_id, code, display_name, is_active
            FROM campuses WHERE tenant_id = @tenantId ORDER BY code;
            """, connection);
        command.Parameters.AddWithValue("@tenantId", tenantId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            campuses.Add(new Campus(reader.GetGuid(0), reader.GetGuid(1),
                reader.GetString(2), reader.GetString(3), reader.GetBoolean(4)));
        return campuses;
    }

    public async Task<bool> IsTenantAdminAsync(TenantIdentity identity, CancellationToken ct)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM tenant_memberships
            WHERE tenant_id = @tenantId AND subject_id = @subjectId
              AND role_code = 'ADMIN' AND is_active = 1);
            """, connection);
        command.Parameters.AddWithValue("@tenantId", identity.TenantId.ToString("D"));
        command.Parameters.AddWithValue("@subjectId", identity.SubjectId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<Campus> CreateCampusAsync(Guid tenantId, string code, string name, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            INSERT INTO campuses (id, tenant_id, code, display_name, is_active)
            VALUES (@id, @tenantId, @code, @name, 1);
            """, connection);
        command.Parameters.AddWithValue("@id", id.ToString("D"));
        command.Parameters.AddWithValue("@tenantId", tenantId.ToString("D"));
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync(ct);
        return new Campus(id, tenantId, code, name, true);
    }
}
