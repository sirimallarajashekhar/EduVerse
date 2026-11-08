using System.Security.Cryptography;
using EduVerse.Application.Administration;
using EduVerse.Application.Identity;
using EduVerse.Api.Modules;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Platform;

public sealed record PlatformInstitution(Guid Id,string Slug,string Name,string Kind,bool Active,string? Plan,int Users,int Campuses);
public sealed record PlatformUser(Guid Id,string Email,string DisplayName,string Role,bool Active);
public sealed record PlatformInvite(Guid TenantId,string Email,string DisplayName,string RoleCode);
public sealed record PlatformDeactivate(Guid TenantId,string Reason);
public sealed record PlatformInviteReceipt(bool ActivationEmailSent);
public sealed record PlatformModuleOverview(Guid TenantId,string Slug,string Name,string Kind,string? PlanCode,IReadOnlyList<TenantModule> Modules);

/// <summary>
/// Platform-operator actions are deliberately explicit. They never impersonate a tenant account
/// or turn a platform JWT into a tenant JWT. Every mutation records the real operator in audit.
/// </summary>
public sealed class PlatformControlStore(string connectionString,IIdentityStore identities,
    IPasswordService passwords,IdentityService identityService,IInstitutionAdminStore administration)
{
    private MySqlConnection Open()=>new(connectionString);
    private static void Add(MySqlCommand cmd,params (string Key,object? Value)[] args)
    {foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value??DBNull.Value);}
    public async Task<IReadOnlyList<PlatformInstitution>> Institutions(CancellationToken ct)
    {
        var list=new List<PlatformInstitution>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT t.id,t.slug,t.display_name,t.kind,t.is_active,s.plan_code,
              (SELECT COUNT(*) FROM tenant_memberships m WHERE m.tenant_id=t.id AND m.is_active=1),
              (SELECT COUNT(*) FROM campuses c WHERE c.tenant_id=t.id AND c.is_active=1)
            FROM tenants t
            LEFT JOIN institution_subscriptions s ON s.tenant_id=t.id
            ORDER BY t.display_name,t.id LIMIT 500
            """,db);
        await using var rd=await cmd.ExecuteReaderAsync(ct);
        while(await rd.ReadAsync(ct))
            list.Add(new PlatformInstitution(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),
              rd.GetString(3),rd.GetBoolean(4),rd.IsDBNull(5)?null:rd.GetString(5),
              Convert.ToInt32(rd.GetInt64(6)),Convert.ToInt32(rd.GetInt64(7))));
        return list;
    }

    public async Task<PlatformModuleOverview?> Modules(Guid tenant,CancellationToken ct)
    {
        if(tenant==Guid.Empty)return null;
        string slug,name,kind;
        await using(var db=Open())
        {
            await db.OpenAsync(ct);
            await using var cmd=new MySqlCommand("""
                SELECT slug,display_name,kind FROM tenants WHERE id=@tenant
                """,db);
            Add(cmd,("@tenant",tenant.ToString("D")));
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))return null;
            slug=reader.GetString(0);name=reader.GetString(1);kind=reader.GetString(2);
        }
        var subscription=await administration.SubscriptionAsync(tenant,ct);
        return new PlatformModuleOverview(tenant,slug,name,kind,subscription.PlanCode,
            TenantModuleCatalog.ForAdmin(kind,subscription.Features));
    }

    public async Task<IReadOnlyList<PlatformUser>?> Users(Guid tenant,CancellationToken ct)
    {
        var list=new List<PlatformUser>();
        await using var db=Open();await db.OpenAsync(ct);
        await using(var exists=new MySqlCommand("SELECT EXISTS(SELECT 1 FROM tenants WHERE id=@tenant)",db))
        {
            Add(exists,("@tenant",tenant.ToString("D")));
            if(Convert.ToInt32(await exists.ExecuteScalarAsync(ct))!=1)return null;
        }
        await using var cmd=new MySqlCommand("""
            SELECT u.id,u.email,u.display_name,m.role_code,m.is_active
            FROM tenant_memberships m
            JOIN identity_users u ON u.id=m.subject_id
            WHERE m.tenant_id=@tenant ORDER BY u.display_name,u.email LIMIT 500
            """,db);
        Add(cmd,("@tenant",tenant.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PlatformUser(reader.GetGuid(0),reader.GetString(1),
              reader.GetString(2),reader.GetString(3),reader.GetBoolean(4)));
        return list;
    }

    public async Task<PlatformInviteReceipt?> Invite(Guid operatorId,PlatformInvite request,CancellationToken ct)
    {
        if(request.TenantId==Guid.Empty||request.RoleCode is not ("ADMIN" or "MEMBER")||
           string.IsNullOrWhiteSpace(request.Email)||request.Email.Length>254||
           !request.Email.Contains('@')||string.IsNullOrWhiteSpace(request.DisplayName)||
           request.DisplayName.Length>160)throw new ArgumentException("Invalid user account.");
        var email=request.Email.Trim();
        await using(var db=Open())
        {
            await db.OpenAsync(ct);
            await using var cmd=new MySqlCommand("SELECT EXISTS(SELECT 1 FROM tenants WHERE id=@tenant AND is_active=1)",db);
            Add(cmd,("@tenant",request.TenantId.ToString("D")));
            if(Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))!=1)return null;
        }
        // No default or operator-visible password. Activation remains one-time password reset.
        var disabledPassword=passwords.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        await identities.CreateUserMembershipAsync(request.TenantId,email,request.DisplayName.Trim(),
            disabledPassword,request.RoleCode,ct);
        await using(var auditDb=Open())
        {
            await auditDb.OpenAsync(ct);
            await using var cmd=new MySqlCommand("""
                INSERT INTO platform_control_audit(operator_id,tenant_id,target_user_id,action_code,reason)
                SELECT @operator,@tenant,u.id,'USER_INVITED',@reason
                FROM identity_users u WHERE u.email_normalized=@email
                """,auditDb);
            Add(cmd,("@operator",operatorId.ToString("D")),("@tenant",request.TenantId.ToString("D")),
                ("@email",email.ToUpperInvariant()),("@reason","Role: "+request.RoleCode));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await identities.AuditAsync(request.TenantId,operatorId,"PLATFORM_USER_INVITED",ct);
        try
        {
            await identityService.RequestResetAsync(email,ct);
            return new PlatformInviteReceipt(true);
        }
        catch(Exception ex) when(ex is not OperationCanceledException)
        {
            // Account remains created; do not hide failed email delivery from the operator.
            await using(var db=Open())
            {
                await db.OpenAsync(ct);
                await using var cmd=new MySqlCommand("""
                    INSERT INTO platform_control_audit(operator_id,tenant_id,target_user_id,action_code)
                    SELECT @operator,@tenant,u.id,'INVITE_EMAIL_FAILED'
                    FROM identity_users u WHERE u.email_normalized=@email
                    """,db);
                Add(cmd,("@operator",operatorId.ToString("D")),
                    ("@tenant",request.TenantId.ToString("D")),("@email",email.ToUpperInvariant()));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            await identities.AuditAsync(request.TenantId,operatorId,"PLATFORM_INVITE_EMAIL_FAILED",ct);
            return new PlatformInviteReceipt(false);
        }
    }

    public async Task<bool> Deactivate(Guid operatorId,Guid tenant,Guid target,string reason,CancellationToken ct)
    {
        if(tenant==Guid.Empty||target==Guid.Empty||string.IsNullOrWhiteSpace(reason)||
           reason.Length>500)throw new ArgumentException("A deactivation reason is required.");
        await using var db=Open();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        string role;
        await using(var check=new MySqlCommand("""
          SELECT role_code FROM tenant_memberships
          WHERE tenant_id=@tenant AND subject_id=@target AND is_active=1 FOR UPDATE
          """,db,tx))
        {
            Add(check,("@tenant",tenant.ToString("D")),("@target",target.ToString("D")));
            var value=await check.ExecuteScalarAsync(ct);
            if(value is null)return false;
            role=Convert.ToString(value)!;
        }
        if(role=="ADMIN")
        {
            await using var last=new MySqlCommand("""
              SELECT COUNT(*) FROM tenant_memberships
              WHERE tenant_id=@tenant AND role_code='ADMIN' AND is_active=1
              """,db,tx);
            Add(last,("@tenant",tenant.ToString("D")));
            if(Convert.ToInt64(await last.ExecuteScalarAsync(ct))<=1)
                throw new InvalidOperationException("Cannot remove the last institution administrator.");
        }
        await using(var change=new MySqlCommand("""
          UPDATE tenant_memberships SET is_active=0
          WHERE tenant_id=@tenant AND subject_id=@target AND is_active=1
          """,db,tx))
        {
            Add(change,("@tenant",tenant.ToString("D")),("@target",target.ToString("D")));
            if(await change.ExecuteNonQueryAsync(ct)!=1)return false;
        }
        await using(var revoke=new MySqlCommand("""
          UPDATE identity_sessions SET revoked_utc=UTC_TIMESTAMP(6)
          WHERE tenant_id=@tenant AND user_id=@target AND revoked_utc IS NULL
          """,db,tx))
        {
            Add(revoke,("@tenant",tenant.ToString("D")),("@target",target.ToString("D")));
            await revoke.ExecuteNonQueryAsync(ct);
        }
        await using(var audit=new MySqlCommand("""
          INSERT INTO identity_audit_events(tenant_id,user_id,event_code)
          VALUES(@tenant,@operator,'PLATFORM_USER_DEACTIVATED')
          """,db,tx))
        {
            Add(audit,("@tenant",tenant.ToString("D")),("@operator",operatorId.ToString("D")));
            await audit.ExecuteNonQueryAsync(ct);
        }
        await using(var detailed=new MySqlCommand("""
          INSERT INTO platform_control_audit(operator_id,tenant_id,target_user_id,action_code,reason)
          VALUES(@operator,@tenant,@target,'USER_DEACTIVATED',@reason)
          """,db,tx))
        {
            Add(detailed,("@operator",operatorId.ToString("D")),("@tenant",tenant.ToString("D")),
                ("@target",target.ToString("D")),("@reason",reason.Trim()));
            await detailed.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
