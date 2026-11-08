using System.Security.Cryptography;
using EduVerse.Application.Identity;
using MySqlConnector;

namespace EduVerse.Api.Platform;

public sealed record PlatformRegistration(Guid Id, string Slug, string Name, string Kind,
    string Email, string Status, DateTime CreatedUtc, DateTime? ReviewedUtc, string? Reason, Guid? TenantId);
public sealed record ReviewCommand(string? Reason);
public sealed record PlatformDecision(Guid RequestId, string Status, Guid? TenantId);
public sealed record PlatformSession(Guid UserId, string Email, string DisplayName, Guid SessionId, DateTimeOffset ExpiresUtc);

/// <summary>Platform privileges are not tenant roles. All decisions are made by API/MySQL.</summary>
public sealed class PlatformApprovalStore(string connectionString, IIdentityStore identities, IPasswordService passwords)
{
    private MySqlConnection Open() => new(connectionString);
    private static void Add(MySqlCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    public async Task<PlatformSession?> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || string.IsNullOrEmpty(password))
            return null;
        var account = await identities.FindAccountAsync(email.Trim().ToUpperInvariant(), ct);
        if (account is null || !account.IsActive || account.LockedUntilUtc > DateTime.UtcNow) return null;
        var valid = account.PasswordHash is not null && passwords.Verify(account.PasswordHash, password, out _);
        if (!valid)
        {
            await identities.RegisterFailureAsync(account.Id, ct);
            return null;
        }
        await using var db = Open();
        await db.OpenAsync(ct);
        await using var check = new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM platform_operators p JOIN identity_users u ON u.id=p.user_id
              WHERE p.user_id=@uid AND p.is_active=1 AND u.is_active=1)
            """,db);
        Add(check,"@uid",account.Id.ToString("D"));
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1) return null;
        await identities.RegisterSuccessAsync(account.Id,ct);
        var sid=Guid.NewGuid();
        var expires=DateTimeOffset.UtcNow.AddMinutes(20);
        await using var cmd=new MySqlCommand("""
            INSERT INTO platform_sessions(id,user_id,expires_utc) VALUES(@sid,@uid,@expires)
            """,db);
        Add(cmd,"@sid",sid.ToString("D"));
        Add(cmd,"@uid",account.Id.ToString("D"));
        Add(cmd,"@expires",expires.UtcDateTime);
        await cmd.ExecuteNonQueryAsync(ct);
        return new PlatformSession(account.Id,account.Email,account.DisplayName,sid,expires);
    }

    public async Task<bool> SessionValidAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await using var db=Open();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM platform_sessions s
              JOIN platform_operators p ON p.user_id=s.user_id AND p.is_active=1
              JOIN identity_users u ON u.id=p.user_id AND u.is_active=1
              WHERE s.id=@sid AND s.user_id=@uid AND s.revoked_utc IS NULL
                AND s.expires_utc>UTC_TIMESTAMP(6))
            """,db);
        Add(cmd,"@sid",sessionId.ToString("D")); Add(cmd,"@uid",userId.ToString("D"));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    public async Task LogoutAsync(Guid userId,Guid sessionId,CancellationToken ct)
    {
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            UPDATE platform_sessions SET revoked_utc=UTC_TIMESTAMP(6)
            WHERE id=@sid AND user_id=@uid AND revoked_utc IS NULL
            """,db);
        Add(cmd,"@sid",sessionId.ToString("D")); Add(cmd,"@uid",userId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PlatformRegistration>> ListAsync(string status,CancellationToken ct)
    {
        if (status is not ("PENDING" or "APPROVED" or "REJECTED")) throw new ArgumentException("Invalid status");
        var list=new List<PlatformRegistration>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
          SELECT id,requested_slug,institution_name,institution_kind,contact_email,status,
                 created_utc,reviewed_utc,review_reason,provisioned_tenant_id
          FROM institution_registration_requests WHERE status=@status
          ORDER BY created_utc,id LIMIT 200
          """,db);
        Add(cmd,"@status",status);
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PlatformRegistration(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetDateTime(6),
                reader.IsDBNull(7)?null:reader.GetDateTime(7),
                reader.IsDBNull(8)?null:reader.GetString(8),
                reader.IsDBNull(9)?null:reader.GetGuid(9)));
        return list;
    }

    public async Task<PlatformDecision?> ReviewAsync(Guid requestId,Guid operatorId,bool approve,string? reason,CancellationToken ct)
    {
        reason=reason?.Trim();
        if (approve && string.IsNullOrWhiteSpace(reason)) reason="Approved after operator review";
        if (string.IsNullOrWhiteSpace(reason) || reason.Length>500)
            throw new ArgumentException("A review reason (1-500 characters) is required.");
        await using var db=Open();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        async Task<int> Execute(string sql,params (string,object?)[] args)
        {
            await using var command=new MySqlCommand(sql,db,tx);
            foreach(var (name,value) in args)Add(command,name,value);
            return await command.ExecuteNonQueryAsync(ct);
        }
        await using var lockCmd=new MySqlCommand("""
            SELECT requested_slug,institution_name,institution_kind,contact_email,status
            FROM institution_registration_requests WHERE id=@id FOR UPDATE
            """,db,tx);
        Add(lockCmd,"@id",requestId.ToString("D"));
        string slug,name,kind,email,status;
        await using(var reader=await lockCmd.ExecuteReaderAsync(ct))
        {
            if(!await reader.ReadAsync(ct))return null;
            slug=reader.GetString(0);name=reader.GetString(1);kind=reader.GetString(2);
            email=reader.GetString(3);status=reader.GetString(4);
        }
        if(status!="PENDING") throw new InvalidOperationException("Registration has already been reviewed.");
        Guid? tenantId=null;
        if(approve)
        {
            tenantId=Guid.NewGuid();
            // Slug uniqueness is protected by tenants.uq_tenants_slug, including concurrent approvals.
            await Execute("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,@name,@kind)",
                ("@id",tenantId.Value.ToString("D")),("@slug",slug),("@name",name),("@kind",kind));
            await Execute("""
                INSERT INTO institution_profiles(tenant_id,legal_name,contact_email,onboarding_status)
                VALUES(@id,@name,@email,'DRAFT')
                """,("@id",tenantId.Value.ToString("D")),("@name",name),("@email",email));
            await Execute("""
                INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@id,'PILOT')
                """,("@id",tenantId.Value.ToString("D")));
            await Execute("""
                INSERT INTO campuses(id,tenant_id,code,display_name)
                VALUES(@campus,@tenant,'MAIN','Main Campus')
                """,("@campus",Guid.NewGuid().ToString("D")),("@tenant",tenantId.Value.ToString("D")));
            var normalized=email.Trim().ToUpperInvariant();
            var adminUserId=Guid.NewGuid();
            // Newly provisioned accounts have an unusable random password, never a default credential.
            // Existing users are reused by normalized email. Account activation uses password reset email.
            var randomPassword=Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            await Execute("""
                INSERT INTO identity_users(id,email,email_normalized,password_hash,display_name)
                VALUES(@uid,@email,@normalized,@hash,@name)
                ON DUPLICATE KEY UPDATE id=id
                """,("@uid",adminUserId.ToString("D")),("@email",email),
                ("@normalized",normalized),("@hash",passwords.Hash(randomPassword)),("@name",name+" Admin"));
            await using var getUser=new MySqlCommand(
                "SELECT id FROM identity_users WHERE email_normalized=@email AND is_active=1",db,tx);
            Add(getUser,"@email",normalized);
            var found=await getUser.ExecuteScalarAsync(ct);
            if(found is null) throw new InvalidOperationException("Administrator email belongs to a disabled account.");
            adminUserId=Guid.Parse(Convert.ToString(found)!);
            await Execute("""
                INSERT INTO tenant_memberships(tenant_id,subject_id,role_code)
                VALUES(@tenant,@uid,'ADMIN')
                """,("@tenant",tenantId.Value.ToString("D")),("@uid",adminUserId.ToString("D")));
        }
        var decision=approve?"APPROVED":"REJECTED";
        await Execute("""
            UPDATE institution_registration_requests
            SET status=@status,reviewed_utc=UTC_TIMESTAMP(6),reviewed_by=@operator,
                review_reason=@reason,provisioned_tenant_id=@tenant
            WHERE id=@request AND status='PENDING'
            """,("@status",decision),("@operator",operatorId.ToString("D")),("@reason",reason),
            ("@tenant",tenantId?.ToString("D")),("@request",requestId.ToString("D")));
        await Execute("""
            INSERT INTO platform_approval_audit(request_id,operator_id,action,reason,tenant_id)
            VALUES(@request,@operator,@status,@reason,@tenant)
            """,("@request",requestId.ToString("D")),("@operator",operatorId.ToString("D")),
            ("@status",decision),("@reason",reason),("@tenant",tenantId?.ToString("D")));
        await tx.CommitAsync(ct);
        return new PlatformDecision(requestId,decision,tenantId);
    }
}
