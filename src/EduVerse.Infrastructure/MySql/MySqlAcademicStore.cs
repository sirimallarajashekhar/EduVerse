using EduVerse.Application.Academic;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>Tenant, campus, plan and live ADMIN status are checked server-side for every operation.</summary>
public sealed partial class MySqlAcademicStore(string connectionString):IAcademicStore
{
    private MySqlConnection Connect()=>new(connectionString);
    private static void Args(MySqlCommand command,params (string Name,object? Value)[] args)
    {
        foreach(var (name,value) in args)command.Parameters.AddWithValue(name,value ?? DBNull.Value);
    }

    public async Task<bool> IsActiveAdminWithAcademicFeatureAsync(TenantIdentity identity,CancellationToken ct)
    {
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(
              SELECT 1 FROM tenant_memberships m
              JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
              JOIN identity_users u ON u.id=@subject AND u.is_active=1
              JOIN institution_subscriptions s ON s.tenant_id=m.tenant_id
                AND s.status='ACTIVE' AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
              JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='academics'
              JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
              WHERE m.tenant_id=@tenant AND m.subject_id=@subject
                AND m.is_active=1 AND m.role_code='ADMIN')
            """,db);
        Args(cmd,("@tenant",identity.TenantId.ToString("D")),("@subject",identity.SubjectId));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    private static async Task<bool> IsActiveCampusAsync(
        MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)
    {
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@tenant AND id=@campus AND is_active=1)
            """,db,tx);
        Args(cmd,("@tenant",tenant.ToString("D")),("@campus",campus.ToString("D")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    private static async Task<bool> HasYearAsync(
        MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid year,CancellationToken ct)
    {
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM academic_years
             WHERE tenant_id=@tenant AND campus_id=@campus AND id=@year AND is_active=1)
            """,db,tx);
        Args(cmd,("@tenant",tenant.ToString("D")),("@campus",campus.ToString("D")),("@year",year.ToString("D")));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    public async Task<IReadOnlyList<AcademicYear>> YearsAsync(Guid tenantId,Guid campusId,CancellationToken ct)
    {
        var result=new List<AcademicYear>();
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,code,display_name,starts_on,ends_on,is_active
            FROM academic_years WHERE tenant_id=@tenant AND campus_id=@campus
            ORDER BY starts_on DESC LIMIT 100
            """,db);
        Args(cmd,("@tenant",tenantId.ToString("D")),("@campus",campusId.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            result.Add(new AcademicYear(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetString(3),reader.GetString(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.GetBoolean(7)));
        return result;
    }

    public async Task<AcademicYear?> CreateYearAsync(Guid tenantId,AcademicYearCreate request,CancellationToken ct)
    {
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await IsActiveCampusAsync(db,tx,tenantId,request.CampusId,ct))return null;
        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO academic_years(id,tenant_id,campus_id,code,display_name,starts_on,ends_on)
            VALUES (@id,@tenant,@campus,@code,@name,@starts,@ends)
            """,db,tx);
        Args(cmd,("@id",id.ToString("D")),("@tenant",tenantId.ToString("D")),
            ("@campus",request.CampusId.ToString("D")),("@code",request.Code),("@name",request.Name),
            ("@starts",request.StartsOn.ToDateTime(TimeOnly.MinValue)),
            ("@ends",request.EndsOn.ToDateTime(TimeOnly.MinValue)));
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new AcademicYear(id,tenantId,request.CampusId,request.Code,request.Name,
            request.StartsOn,request.EndsOn,true);
    }

    private static AcademicItem ReadItem(MySqlDataReader r) =>
        new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetString(5),
            r.IsDBNull(6)?null:r.GetGuid(6),r.IsDBNull(7)?null:r.GetGuid(7),
            r.IsDBNull(8)?null:r.GetGuid(8),
            r.IsDBNull(9)?null:DateOnly.FromDateTime(r.GetDateTime(9)),
            r.IsDBNull(10)?null:DateOnly.FromDateTime(r.GetDateTime(10)),r.GetBoolean(11));

    public async Task<IReadOnlyList<AcademicItem>> ItemsAsync(Guid tenantId,Guid campusId,string? kind,CancellationToken ct)
    {
        var result=new List<AcademicItem>();
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,kind,code,display_name,parent_id,academic_year_id,
                   department_id,starts_on,ends_on,is_active
            FROM academic_catalog
            WHERE tenant_id=@tenant AND campus_id=@campus
              AND (@kind IS NULL OR kind=@kind)
            ORDER BY kind,code LIMIT 500
            """,db);
        Args(cmd,("@tenant",tenantId.ToString("D")),("@campus",campusId.ToString("D")),("@kind",kind));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))result.Add(ReadItem(reader));
        return result;
    }

    private static bool ParentKindAllowed(string kind,string? parent) => kind switch
    {
        "SECTION" => parent is "CLASS" or "GRADE",
        "COURSE" or "BATCH" => parent=="PROGRAM",
        "CLASS" => parent is null or "GRADE",
        "SUBJECT" => parent is null or "COURSE" or "CLASS" or "GRADE" or "PROGRAM",
        "SEMESTER" => parent is null or "PROGRAM",
        _ => parent is null
    };

    public async Task<AcademicItem?> CreateItemAsync(Guid tenantId,AcademicItemCreate request,CancellationToken ct)
    {
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await IsActiveCampusAsync(db,tx,tenantId,request.CampusId,ct))return null;
        if(request.AcademicYearId is Guid year &&
           !await HasYearAsync(db,tx,tenantId,request.CampusId,year,ct))return null;
        string? parentKind=null;
        if(request.ParentId is Guid parent)
        {
            await using var check=new MySqlCommand("""
                SELECT kind FROM academic_catalog
                WHERE id=@parent AND tenant_id=@tenant AND campus_id=@campus AND is_active=1
                """,db,tx);
            Args(check,("@parent",parent.ToString("D")),("@tenant",tenantId.ToString("D")),
                ("@campus",request.CampusId.ToString("D")));
            parentKind=await check.ExecuteScalarAsync(ct) as string;
            if(parentKind is null)return null;
        }
        if(!ParentKindAllowed(request.Kind,parentKind))return null;
        if(request.DepartmentId is Guid department)
        {
            await using var check=new MySqlCommand("""
                SELECT EXISTS(SELECT 1 FROM institution_departments
                WHERE id=@department AND tenant_id=@tenant AND campus_id=@campus AND is_active=1)
                """,db,tx);
            Args(check,("@department",department.ToString("D")),("@tenant",tenantId.ToString("D")),
                ("@campus",request.CampusId.ToString("D")));
            if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)return null;
        }

        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO academic_catalog(id,tenant_id,campus_id,kind,code,display_name,parent_id,
            academic_year_id,department_id,starts_on,ends_on)
            VALUES (@id,@tenant,@campus,@kind,@code,@name,@parent,@year,@department,@starts,@ends)
            """,db,tx);
        Args(cmd,("@id",id.ToString("D")),("@tenant",tenantId.ToString("D")),
            ("@campus",request.CampusId.ToString("D")),("@kind",request.Kind),
            ("@code",request.Code),("@name",request.Name),
            ("@parent",request.ParentId?.ToString("D")),("@year",request.AcademicYearId?.ToString("D")),
            ("@department",request.DepartmentId?.ToString("D")),
            ("@starts",request.StartsOn?.ToDateTime(TimeOnly.MinValue)),
            ("@ends",request.EndsOn?.ToDateTime(TimeOnly.MinValue)));
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new AcademicItem(id,tenantId,request.CampusId,request.Kind,request.Code,request.Name,
            request.ParentId,request.AcademicYearId,request.DepartmentId,request.StartsOn,request.EndsOn,true);
    }
}
