using EduVerse.Application.Identity;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed class MySqlAdvancedRbacStore(string connectionString):IAdvancedRbacStore
{
    private MySqlConnection Open()=>new(connectionString);
    private static void Add(MySqlCommand cmd,params (string Key,object? Value)[] values)
    {
        foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value??DBNull.Value);
    }
    public async Task<IReadOnlyList<RbacRole>> ListRolesAsync(Guid tenantId,CancellationToken ct)
    {
        var results=new List<RbacRole>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT r.code,r.display_name,p.permission_code
            FROM rbac_roles r
            LEFT JOIN rbac_role_permissions p ON p.tenant_id=r.tenant_id AND p.role_code=r.code
            WHERE r.tenant_id=@tenant ORDER BY r.code,p.permission_code LIMIT 3000
            """,db);
        Add(cmd,("@tenant",tenantId.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        string? previous=null;string name="";var perms=new List<string>();
        while(await reader.ReadAsync(ct))
        {
            var code=reader.GetString(0);
            if(previous!=code)
            {
                if(previous is not null)results.Add(new RbacRole(previous,name,perms.ToArray()));
                previous=code;name=reader.GetString(1);perms=new List<string>();
            }
            if(!reader.IsDBNull(2))perms.Add(reader.GetString(2));
        }
        if(previous is not null)results.Add(new RbacRole(previous,name,perms.ToArray()));
        return results;
    }
    public async Task<IReadOnlyList<RbacUserRole>> ListAssignmentsAsync(Guid tenantId,CancellationToken ct)
    {
        var list=new List<RbacUserRole>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT a.subject_id,a.role_code FROM rbac_user_roles a
            JOIN tenant_memberships m ON m.tenant_id=a.tenant_id AND m.subject_id=a.subject_id AND m.is_active=1
            WHERE a.tenant_id=@tenant ORDER BY a.subject_id,a.role_code LIMIT 3000
            """,db);
        Add(cmd,("@tenant",tenantId.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))list.Add(new RbacUserRole(reader.GetString(0),reader.GetString(1)));
        return list;
    }
    public async Task CreateRoleAsync(Guid tenantId,string code,string displayName,Guid actorId,CancellationToken ct)
    {
        await using var db=Open();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        await using(var cmd=new MySqlCommand("""
            INSERT INTO rbac_roles(tenant_id,code,display_name) VALUES(@tid,@code,@display)
            """,db,tx))
        {
            Add(cmd,("@tid",tenantId.ToString("D")),("@code",code),("@display",displayName));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await Audit(db,tx,tenantId,actorId,"RBAC_ROLE_CREATED",ct);
        await tx.CommitAsync(ct);
    }
    public async Task ReplacePermissionsAsync(Guid tenantId,string code,IReadOnlyList<string> permissions,Guid actorId,CancellationToken ct)
    {
        await using var db=Open();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        await using(var exists=new MySqlCommand("""
            SELECT code FROM rbac_roles WHERE tenant_id=@tenant AND code=@role FOR UPDATE
            """,db,tx))
        {
            Add(exists,("@tenant",tenantId.ToString("D")),("@role",code));
            if(await exists.ExecuteScalarAsync(ct) is null)throw new KeyNotFoundException("Role not found.");
        }
        await using(var clear=new MySqlCommand("""
            DELETE FROM rbac_role_permissions WHERE tenant_id=@tenant AND role_code=@role
            """,db,tx))
        {
            Add(clear,("@tenant",tenantId.ToString("D")),("@role",code));
            await clear.ExecuteNonQueryAsync(ct);
        }
        foreach(var permission in permissions)
        {
            await using var insert=new MySqlCommand("""
                INSERT INTO rbac_role_permissions(tenant_id,role_code,permission_code)
                VALUES(@tenant,@role,@permission)
                """,db,tx);
            Add(insert,("@tenant",tenantId.ToString("D")),("@role",code),("@permission",permission));
            await insert.ExecuteNonQueryAsync(ct);
        }
        // Force fresh login after a role permission change; existing bearer tokens cannot preserve stale privileges.
        await using(var revoke=new MySqlCommand("""
            UPDATE identity_sessions s
            JOIN rbac_user_roles a ON a.tenant_id=s.tenant_id
                AND a.subject_id=CAST(s.user_id AS CHAR(36))
            SET s.revoked_utc=UTC_TIMESTAMP(6)
            WHERE s.tenant_id=@tenant AND a.role_code=@role AND s.revoked_utc IS NULL
            """,db,tx))
        {
            Add(revoke,("@tenant",tenantId.ToString("D")),("@role",code));
            await revoke.ExecuteNonQueryAsync(ct);
        }
        await Audit(db,tx,tenantId,actorId,"RBAC_PERMISSIONS_UPDATED",ct);
        await tx.CommitAsync(ct);
    }
    public async Task<bool> SetAssignmentAsync(Guid tenantId,Guid userId,string code,bool assign,Guid actorId,CancellationToken ct)
    {
        await using var db=Open();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        await using(var exists=new MySqlCommand("""
            SELECT code FROM rbac_roles WHERE tenant_id=@tenant AND code=@role FOR UPDATE
            """,db,tx))
        {
            Add(exists,("@tenant",tenantId.ToString("D")),("@role",code));
            if(await exists.ExecuteScalarAsync(ct) is null)throw new KeyNotFoundException("Role not found.");
        }
        int count;
        if(assign)
        {
            await using var cmd=new MySqlCommand("""
                INSERT IGNORE INTO rbac_user_roles(tenant_id,subject_id,role_code)
                SELECT m.tenant_id,m.subject_id,@role FROM tenant_memberships m
                JOIN identity_users u ON u.id=@user AND u.is_active=1
                WHERE m.tenant_id=@tenant AND m.subject_id=@user AND m.is_active=1
                  AND m.role_code='MEMBER'
                """,db,tx);
            Add(cmd,("@tenant",tenantId.ToString("D")),("@user",userId.ToString("D")),("@role",code));
            count=await cmd.ExecuteNonQueryAsync(ct);
        }
        else
        {
            await using var cmd=new MySqlCommand("""
                DELETE FROM rbac_user_roles WHERE tenant_id=@tenant AND subject_id=@user AND role_code=@role
                """,db,tx);
            Add(cmd,("@tenant",tenantId.ToString("D")),("@user",userId.ToString("D")),("@role",code));
            count=await cmd.ExecuteNonQueryAsync(ct);
        }
        if(count>0)
        {
            await using var revoke=new MySqlCommand("""
                UPDATE identity_sessions SET revoked_utc=UTC_TIMESTAMP(6)
                WHERE tenant_id=@tenant AND user_id=@user AND revoked_utc IS NULL
                """,db,tx);
            Add(revoke,("@tenant",tenantId.ToString("D")),("@user",userId.ToString("D")));
            await revoke.ExecuteNonQueryAsync(ct);
            await Audit(db,tx,tenantId,actorId,assign?"RBAC_ROLE_ASSIGNED":"RBAC_ROLE_REVOKED",ct);
        }
        await tx.CommitAsync(ct);
        return count>0;
    }
    private static async Task Audit(MySqlConnection db,MySqlTransaction tx,Guid tenantId,Guid actorId,string code,CancellationToken ct)
    {
        await using var cmd=new MySqlCommand("""
            INSERT INTO identity_audit_events(tenant_id,user_id,event_code) VALUES(@tenant,@actor,@code)
            """,db,tx);
        Add(cmd,("@tenant",tenantId.ToString("D")),("@actor",actorId.ToString("D")),("@code",code));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
