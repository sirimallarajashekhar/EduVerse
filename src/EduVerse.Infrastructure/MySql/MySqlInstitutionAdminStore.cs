using EduVerse.Application.Administration;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

/// <summary>Tenant-scoped institution operations. SQL identifiers are fixed, never caller-supplied.</summary>
public sealed partial class MySqlInstitutionAdminStore(string connectionString) : IInstitutionAdminStore
{
    private MySqlConnection Connection() => new(connectionString);
    private static void Add(MySqlCommand command, params (string Name, object? Value)[] args)
    {
        foreach (var (key,value) in args)
            command.Parameters.AddWithValue(key,value ?? DBNull.Value);
    }
    private static string Table(string resource) => resource switch
    {
        "departments" => "institution_departments",
        "buildings" => "institution_buildings",
        _ => throw new ArgumentException("Unsupported resource.")
    };

    public async Task<Guid> SubmitRegistrationAsync(RegistrationRequest request, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        await using var db = Connection();
        await db.OpenAsync(ct);
        await using var command = new MySqlCommand("""
            INSERT INTO institution_registration_requests
              (id,requested_slug,institution_name,institution_kind,contact_email)
            VALUES (@id,@slug,@name,@kind,@email)
            """,db);
        Add(command,("@id",id.ToString("D")),("@slug",request.Slug),
            ("@name",request.InstitutionName),("@kind",request.Kind),("@email",request.ContactEmail));
        await command.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<bool> IsTenantAdminAsync(TenantIdentity identity, CancellationToken ct)
    {
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM tenant_memberships m
              JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
              JOIN identity_users u ON u.id=@subject AND u.is_active=1
              WHERE m.tenant_id=@tenant AND m.subject_id=@subject
                AND m.role_code='ADMIN' AND m.is_active=1)
            """,db);
        Add(command,("@tenant",identity.TenantId.ToString("D")),("@subject",identity.SubjectId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct))==1;
    }

    public async Task<InstitutionProfile?> ProfileAsync(Guid tenantId, CancellationToken ct)
    {
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            SELECT legal_name,contact_email,contact_phone,address_line,city,state_region,
              postal_code,onboarding_status
            FROM institution_profiles WHERE tenant_id=@tenant
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")));
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))return null;
        return new InstitutionProfile(reader.GetString(0),reader.GetString(1),
            reader.IsDBNull(2)?null:reader.GetString(2),
            reader.IsDBNull(3)?null:reader.GetString(3),
            reader.IsDBNull(4)?null:reader.GetString(4),
            reader.IsDBNull(5)?null:reader.GetString(5),
            reader.IsDBNull(6)?null:reader.GetString(6),reader.GetString(7));
    }

    public async Task SaveProfileAsync(Guid tenantId, ProfileUpdate profile, CancellationToken ct)
    {
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            INSERT INTO institution_profiles
              (tenant_id,legal_name,contact_email,contact_phone,address_line,city,state_region,postal_code)
            VALUES (@tenant,@name,@email,@phone,@address,@city,@state,@postal)
            ON DUPLICATE KEY UPDATE legal_name=VALUES(legal_name),
              contact_email=VALUES(contact_email),contact_phone=VALUES(contact_phone),
              address_line=VALUES(address_line),city=VALUES(city),
              state_region=VALUES(state_region),postal_code=VALUES(postal_code)
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")),("@name",profile.LegalName),
            ("@email",profile.ContactEmail),("@phone",profile.ContactPhone),
            ("@address",profile.AddressLine),("@city",profile.City),
            ("@state",profile.StateRegion),("@postal",profile.PostalCode));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkReadyAsync(Guid tenantId,CancellationToken ct)
    {
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            UPDATE institution_profiles SET onboarding_status='READY'
            WHERE tenant_id=@tenant AND legal_name<>'' AND contact_email<>''
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CampusResource>> ResourcesAsync(
        Guid tenantId,Guid? campusId,string type,CancellationToken ct)
    {
        var items=new List<CampusResource>();
        await using var db=Connection();
        await db.OpenAsync(ct);
        // Whitelisted table identifier; tenant predicate always present.
        await using var command=new MySqlCommand($"""
            SELECT id,tenant_id,campus_id,code,display_name,is_active FROM {Table(type)}
            WHERE tenant_id=@tenant AND (@campus IS NULL OR campus_id=@campus)
            ORDER BY code LIMIT 500
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")),("@campus",campusId?.ToString("D")));
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            items.Add(new CampusResource(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetString(3),reader.GetString(4),reader.GetBoolean(5)));
        return items;
    }

    public async Task<CampusResource?> CreateResourceAsync(
        Guid tenantId,ResourceCreate request,string type,CancellationToken ct)
    {
        var id=Guid.NewGuid();
        await using var db=Connection();
        await db.OpenAsync(ct);
        // INSERT...SELECT additionally proves the campus belongs to this tenant and is active.
        await using var command=new MySqlCommand($"""
            INSERT INTO {Table(type)}(id,tenant_id,campus_id,code,display_name,is_active)
            SELECT @id,c.tenant_id,c.id,@code,@name,1 FROM campuses c
            WHERE c.id=@campus AND c.tenant_id=@tenant AND c.is_active=1
            """,db);
        Add(command,("@id",id.ToString("D")),("@tenant",tenantId.ToString("D")),
            ("@campus",request.CampusId.ToString("D")),("@code",request.Code),("@name",request.Name));
        return await command.ExecuteNonQueryAsync(ct)==1
            ? new CampusResource(id,tenantId,request.CampusId,request.Code,request.Name,true)
            : null;
    }

    public async Task<IReadOnlyList<InstitutionSetting>> SettingsAsync(Guid tenantId,CancellationToken ct)
    {
        var settings=new List<InstitutionSetting>();
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            SELECT setting_key,setting_value FROM institution_settings
            WHERE tenant_id=@tenant ORDER BY setting_key
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")));
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            settings.Add(new InstitutionSetting(reader.GetString(0),reader.GetString(1)));
        return settings;
    }

    public async Task SaveSettingAsync(Guid tenantId,string key,string value,CancellationToken ct)
    {
        await using var db=Connection();
        await db.OpenAsync(ct);
        await using var command=new MySqlCommand("""
            INSERT INTO institution_settings(tenant_id,setting_key,setting_value)
            VALUES (@tenant,@key,@value)
            ON DUPLICATE KEY UPDATE setting_value=VALUES(setting_value)
            """,db);
        Add(command,("@tenant",tenantId.ToString("D")),("@key",key),("@value",value));
        await command.ExecuteNonQueryAsync(ct);
    }
}
