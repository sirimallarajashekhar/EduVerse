using EduVerse.Application.Identity;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>Identity records are tenant-bound. SQL is always parameterized and authorization is rechecked server-side.</summary>
public sealed class MySqlIdentityStore(string connectionString) : IIdentityStore
{
    private MySqlConnection Connection() => new(connectionString);
    private static void Add(MySqlCommand c, params (string Name, object? Value)[] args)
    {
        foreach (var (name, value) in args) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public async Task<AccountRecord?> FindAccountAsync(string normalizedEmail, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT id,email,display_name,password_hash,is_active,failed_logins,locked_until_utc
            FROM identity_users WHERE email_normalized=@email LIMIT 1
            """, db);
        Add(cmd, ("@email", normalizedEmail));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new AccountRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4), reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6));
    }

    public async Task<MembershipRecord?> FindMembershipAsync(Guid userId, string tenantSlug, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT t.id,m.subject_id,m.role_code FROM tenant_memberships m
            INNER JOIN tenants t ON t.id=m.tenant_id
            WHERE t.slug=@slug AND t.is_active=1 AND m.is_active=1 AND m.subject_id=@user
            LIMIT 1
            """, db);
        Add(cmd, ("@slug", tenantSlug), ("@user", userId.ToString("D")));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new MembershipRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    public async Task<bool> IsSessionValidAsync(Guid sessionId, Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT EXISTS(
              SELECT 1 FROM identity_sessions s
              JOIN identity_users u ON u.id=s.user_id
              JOIN tenants t ON t.id=s.tenant_id
              JOIN tenant_memberships m ON m.tenant_id=s.tenant_id
                                    AND m.subject_id=CAST(s.user_id AS CHAR(36))
              WHERE s.id=@sid AND s.user_id=@uid AND s.tenant_id=@tid
                AND s.revoked_utc IS NULL AND s.expires_utc>UTC_TIMESTAMP(6)
                AND u.is_active=1 AND t.is_active=1 AND m.is_active=1)
            """, db);
        Add(cmd, ("@sid", sessionId.ToString("D")), ("@uid", userId.ToString("D")),
            ("@tid", tenantId.ToString("D")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task CreateSessionAsync(Guid sessionId, Guid tenantId, Guid userId, DateTime expiresUtc, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO identity_sessions(id,user_id,tenant_id,expires_utc)
            VALUES (@sid,@uid,@tid,@exp)
            """, db);
        Add(cmd, ("@sid", sessionId.ToString("D")), ("@uid", userId.ToString("D")),
            ("@tid", tenantId.ToString("D")), ("@exp", expiresUtc));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeSessionAsync(Guid sessionId, Guid userId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            UPDATE identity_sessions SET revoked_utc=UTC_TIMESTAMP(6)
            WHERE id=@sid AND user_id=@uid AND revoked_utc IS NULL
            """, db);
        Add(cmd, ("@sid", sessionId.ToString("D")), ("@uid", userId.ToString("D")));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RegisterFailureAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            UPDATE identity_users SET failed_logins=failed_logins+1,
            locked_until_utc=IF(failed_logins+1>=5, UTC_TIMESTAMP(6)+INTERVAL 15 MINUTE, locked_until_utc)
            WHERE id=@uid
            """, db);
        Add(cmd, ("@uid", userId.ToString("D")));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RegisterSuccessAsync(Guid userId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            UPDATE identity_users SET failed_logins=0,locked_until_utc=NULL WHERE id=@uid
            """, db);
        Add(cmd, ("@uid", userId.ToString("D")));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AuditAsync(Guid? tenantId, Guid? userId, string eventCode, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO identity_audit_events(tenant_id,user_id,event_code) VALUES (@tid,@uid,@event)
            """, db);
        Add(cmd, ("@tid", tenantId?.ToString("D")), ("@uid", userId?.ToString("D")), ("@event", eventCode));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveResetTokenAsync(Guid userId, byte[] sha256, DateTime expiresUtc, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT INTO identity_reset_tokens(token_hash,user_id,expires_utc)
            VALUES (@hash,@uid,@exp)
            """, db);
        Add(cmd, ("@hash", sha256), ("@uid", userId.ToString("D")), ("@exp", expiresUtc));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Guid?> ConsumeResetTokenAsync(byte[] sha256, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var select = new MySqlCommand("""
            SELECT user_id FROM identity_reset_tokens
            WHERE token_hash=@hash AND used_utc IS NULL AND expires_utc>UTC_TIMESTAMP(6)
            FOR UPDATE
            """, db, tx);
        Add(select, ("@hash", sha256));
        var scalar = await select.ExecuteScalarAsync(ct);
        if (scalar is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }
        var userId = scalar is Guid guid ? guid : Guid.Parse(Convert.ToString(scalar)!);
        await using var update = new MySqlCommand("""
            UPDATE identity_reset_tokens SET used_utc=UTC_TIMESTAMP(6) WHERE token_hash=@hash
            """, db, tx);
        Add(update, ("@hash", sha256));
        await update.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return userId;
    }

    public async Task SetPasswordAsync(Guid userId, string hash, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var update = new MySqlCommand("""
            UPDATE identity_users SET password_hash=@hash,failed_logins=0,locked_until_utc=NULL WHERE id=@uid
            """, db, tx);
        Add(update, ("@hash", hash), ("@uid", userId.ToString("D")));
        await update.ExecuteNonQueryAsync(ct);
        await using var revoke = new MySqlCommand("""
            UPDATE identity_sessions SET revoked_utc=UTC_TIMESTAMP(6)
            WHERE user_id=@uid AND revoked_utc IS NULL
            """, db, tx);
        Add(revoke, ("@uid", userId.ToString("D")));
        await revoke.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<bool> IsAdminAsync(TenantIdentity identity, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM tenant_memberships m
              JOIN tenants t ON t.id=m.tenant_id
              JOIN identity_users u ON u.id=@uid
              WHERE m.tenant_id=@tid AND m.subject_id=@subject
                AND m.role_code='ADMIN' AND m.is_active=1 AND t.is_active=1 AND u.is_active=1)
            """, db);
        Add(cmd, ("@tid", identity.TenantId.ToString("D")), ("@subject", identity.SubjectId),
            ("@uid", identity.SubjectId));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<bool> DeactivateMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        await using var membership = new MySqlCommand("""
            UPDATE tenant_memberships SET is_active=0
            WHERE tenant_id=@tid AND subject_id=@uid AND is_active=1
            """, db, tx);
        Add(membership, ("@tid", tenantId.ToString("D")), ("@uid", userId.ToString("D")));
        var affected = await membership.ExecuteNonQueryAsync(ct);
        if (affected > 0)
        {
            await using var revoke = new MySqlCommand("""
                UPDATE identity_sessions SET revoked_utc=UTC_TIMESTAMP(6)
                WHERE tenant_id=@tid AND user_id=@uid AND revoked_utc IS NULL
                """, db, tx);
            Add(revoke, ("@tid", tenantId.ToString("D")), ("@uid", userId.ToString("D")));
            await revoke.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<AccountSummary>> ListUsersAsync(Guid tenantId, CancellationToken ct)
    {
        var users = new List<AccountSummary>();
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT u.id,u.email,u.display_name,m.role_code
            FROM tenant_memberships m
            JOIN identity_users u ON u.id=m.subject_id
            WHERE m.tenant_id=@tid AND m.is_active=1 AND u.is_active=1
            ORDER BY u.display_name LIMIT 500
            """, db);
        Add(cmd, ("@tid", tenantId.ToString("D")));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            users.Add(new AccountSummary(reader.GetGuid(0).ToString("D"),
                reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return users;
    }

    public async Task CreateUserMembershipAsync(Guid tenantId, string email, string displayName,
        string passwordHash, string role, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(ct);
        var normalized = email.ToUpperInvariant();
        Guid id;
        await using (var select = new MySqlCommand(
            "SELECT id FROM identity_users WHERE email_normalized=@email FOR UPDATE", db, tx))
        {
            Add(select, ("@email", normalized));
            var scalar = await select.ExecuteScalarAsync(ct);
            id = scalar is null ? Guid.NewGuid()
                : scalar is Guid g ? g : Guid.Parse(Convert.ToString(scalar)!);
            if (scalar is null)
            {
                await using var create = new MySqlCommand("""
                    INSERT INTO identity_users(id,email,email_normalized,password_hash,display_name)
                    VALUES(@id,@email,@normalized,@hash,@display)
                    """, db, tx);
                Add(create, ("@id", id.ToString("D")), ("@email", email),
                    ("@normalized", normalized), ("@hash", passwordHash), ("@display", displayName));
                await create.ExecuteNonQueryAsync(ct);
            }
        }
        await using var membership = new MySqlCommand("""
            INSERT INTO tenant_memberships(tenant_id,subject_id,role_code,is_active)
            VALUES (@tid,@subject,@role,1)
            """, db, tx);
        Add(membership, ("@tid", tenantId.ToString("D")), ("@subject", id.ToString("D")), ("@role", role));
        await membership.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task AddRolePermissionAsync(Guid tenantId, string permission, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT IGNORE INTO identity_role_permissions(tenant_id,role_code,permission_code)
            VALUES (@tid,'MEMBER',@permission)
            """, db);
        Add(cmd, ("@tid", tenantId.ToString("D")), ("@permission", permission));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> GrantCampusAsync(Guid tenantId, string subjectId, Guid campusId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            INSERT IGNORE INTO identity_campus_access(tenant_id,subject_id,campus_id)
            SELECT m.tenant_id,m.subject_id,c.id
            FROM tenant_memberships m JOIN campuses c ON c.tenant_id=m.tenant_id
            WHERE m.tenant_id=@tid AND m.subject_id=@subject AND m.is_active=1
              AND c.id=@campus AND c.is_active=1
            """, db);
        Add(cmd, ("@tid", tenantId.ToString("D")), ("@subject", subjectId), ("@campus", campusId.ToString("D")));
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> HasPermissionAsync(TenantIdentity identity, string permission, Guid? campusId, CancellationToken ct)
    {
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var cmd = new MySqlCommand("""
            SELECT EXISTS (
              SELECT 1 FROM tenant_memberships m
              JOIN tenants t ON t.id=m.tenant_id
              JOIN identity_users u ON u.id=@uid
              WHERE m.tenant_id=@tid AND m.subject_id=@uid AND m.is_active=1
                AND t.is_active=1 AND u.is_active=1
                AND (m.role_code='ADMIN' OR EXISTS (
                    SELECT 1 FROM identity_role_permissions p
                    WHERE p.tenant_id=m.tenant_id AND p.role_code=m.role_code AND p.permission_code=@permission))
                AND (@campus IS NULL OR (m.role_code='ADMIN' OR EXISTS (
                    SELECT 1 FROM identity_campus_access x
                    WHERE x.tenant_id=m.tenant_id AND x.subject_id=m.subject_id AND x.campus_id=@campus)))
              )
            """, db);
        Add(cmd, ("@tid", identity.TenantId.ToString("D")), ("@uid", identity.SubjectId),
            ("@permission", permission), ("@campus", campusId?.ToString("D")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }
}
