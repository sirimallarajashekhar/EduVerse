using EduVerse.Application.Students;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlStudentStore(string connectionString):IStudentStore
{
    private MySqlConnection Connect()=>new(connectionString);
    private static void Add(MySqlCommand c,params (string Key,object? Value)[] args)
    { foreach(var (key,value) in args)c.Parameters.AddWithValue(key,value??DBNull.Value); }
    private static string Id(Guid x)=>x.ToString("D");

    public async Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM tenant_memberships m
            JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
            JOIN identity_users u ON u.id=@user AND u.is_active=1
            JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
              AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
            JOIN subscription_plan_features p ON p.plan_code=s.plan_code AND p.feature_code='core'
            WHERE m.tenant_id=@tenant AND m.subject_id=@user AND m.is_active=1 AND m.role_code='ADMIN')
            """,db);
        Add(cmd,("@tenant",Id(actor.TenantId)),("@user",actor.SubjectId));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    private static async Task<bool> ActiveCampus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)
    {
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@tenant AND id=@campus AND is_active=1)
            """,db,tx);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    private static async Task<bool> ActiveStudent(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid student,CancellationToken ct)
    {
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(SELECT 1 FROM students WHERE tenant_id=@tenant
              AND campus_id=@campus AND id=@student AND status='ACTIVE')
            """,db,tx);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }

    public async Task<AdmissionRecord?> SubmitAsync(Guid tenant,AdmissionSubmit request,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await ActiveCampus(db,tx,tenant,request.CampusId,ct))return null;
        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO admission_applications
              (id,tenant_id,campus_id,given_name,family_name,date_of_birth,contact_email,contact_phone)
            VALUES(@id,@tenant,@campus,@given,@family,@dob,@email,@phone)
            """,db,tx);
        Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(request.CampusId)),
            ("@given",request.GivenName),("@family",request.FamilyName),
            ("@dob",request.DateOfBirth.ToDateTime(TimeOnly.MinValue)),("@email",request.Email),("@phone",request.Phone));
        await cmd.ExecuteNonQueryAsync(ct);
        long number;
        await using (var seq=new MySqlCommand("SELECT LAST_INSERT_ID()",db,tx))
            number=Convert.ToInt64(await seq.ExecuteScalarAsync(ct));
        await tx.CommitAsync(ct);
        return new AdmissionRecord(id,tenant,request.CampusId,number,
            request.GivenName,request.FamilyName,request.DateOfBirth,"SUBMITTED");
    }

    private static AdmissionRecord ReadAdmission(MySqlDataReader r)=>
        new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetInt64(3),r.GetString(4),
            r.GetString(5),DateOnly.FromDateTime(r.GetDateTime(6)),r.GetString(7));

    public async Task<IReadOnlyList<AdmissionRecord>> ApplicationsAsync(Guid tenant,Guid campus,CancellationToken ct)
    {
        var records=new List<AdmissionRecord>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,application_number,given_name,family_name,date_of_birth,status
            FROM admission_applications WHERE tenant_id=@tenant AND campus_id=@campus
            ORDER BY created_utc DESC,application_number DESC LIMIT 200
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))records.Add(ReadAdmission(r));
        return records;
    }

    public async Task<bool> DecideAsync(Guid tenant,Guid campus,Guid id,string decision,Guid actor,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            UPDATE admission_applications
            SET status=@decision,reviewed_by=@actor,reviewed_utc=UTC_TIMESTAMP(6)
            WHERE id=@id AND tenant_id=@tenant AND campus_id=@campus AND status='SUBMITTED'
            """,db);
        Add(cmd,("@decision",decision),("@actor",Id(actor)),("@id",Id(id)),
            ("@tenant",Id(tenant)),("@campus",Id(campus)));
        return await cmd.ExecuteNonQueryAsync(ct)==1;
    }

    private static StudentRecord ReadStudent(MySqlDataReader r)=>
        new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),
            r.GetString(5),DateOnly.FromDateTime(r.GetDateTime(6)),r.GetString(7));

    public async Task<StudentRecord?> CreateStudentAsync(Guid tenant,StudentCreate request,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await ActiveCampus(db,tx,tenant,request.CampusId,ct))return null;
        if(request.ApplicationId is Guid app)
        {
            await using var verify=new MySqlCommand("""
                SELECT given_name,family_name,date_of_birth FROM admission_applications
                WHERE tenant_id=@tenant AND campus_id=@campus AND id=@app AND status='APPROVED' FOR UPDATE
                """,db,tx);
            Add(verify,("@tenant",Id(tenant)),("@campus",Id(request.CampusId)),("@app",Id(app)));
            await using var reader=await verify.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))return null;
            if(!string.Equals(request.GivenName,reader.GetString(0),StringComparison.Ordinal)||
               !string.Equals(request.FamilyName,reader.GetString(1),StringComparison.Ordinal)||
               request.DateOfBirth!=DateOnly.FromDateTime(reader.GetDateTime(2)))return null;
        }
        // Immutable, collision-resistant campus-prefixed admission number.
        var id=Guid.NewGuid();
        var number="EV-"+id.ToString("N").ToUpperInvariant();
        await using (var insert=new MySqlCommand("""
            INSERT INTO students(id,tenant_id,campus_id,application_id,admission_number,given_name,family_name,date_of_birth)
            VALUES (@id,@tenant,@campus,@app,@number,@given,@family,@dob)
            """,db,tx))
        {
            Add(insert,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(request.CampusId)),
                ("@app",request.ApplicationId?.ToString("D")),("@number",number),
                ("@given",request.GivenName),("@family",request.FamilyName),
                ("@dob",request.DateOfBirth.ToDateTime(TimeOnly.MinValue)));
            await insert.ExecuteNonQueryAsync(ct);
        }
        if(request.ApplicationId is Guid application)
        {
            await using var update=new MySqlCommand("""
                UPDATE admission_applications SET status='ENROLLED'
                WHERE tenant_id=@tenant AND campus_id=@campus AND id=@app AND status='APPROVED'
                """,db,tx);
            Add(update,("@tenant",Id(tenant)),("@campus",Id(request.CampusId)),("@app",Id(application)));
            if(await update.ExecuteNonQueryAsync(ct)!=1)throw new InvalidOperationException("Admission concurrently changed.");
        }
        await tx.CommitAsync(ct);
        return new StudentRecord(id,tenant,request.CampusId,number,request.GivenName,
            request.FamilyName,request.DateOfBirth,"ACTIVE");
    }

    public async Task<IReadOnlyList<StudentRecord>> StudentsAsync(Guid tenant,Guid campus,CancellationToken ct)
    {
        var students=new List<StudentRecord>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,admission_number,given_name,family_name,date_of_birth,status
            FROM students WHERE tenant_id=@tenant AND campus_id=@campus ORDER BY created_utc DESC LIMIT 200
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))students.Add(ReadStudent(reader));
        return students;
    }

    public async Task<StudentRecord?> StudentAsync(Guid tenant,Guid campus,Guid id,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,admission_number,given_name,family_name,date_of_birth,status
            FROM students WHERE tenant_id=@tenant AND campus_id=@campus AND id=@student
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(id)));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?ReadStudent(r):null;
    }
}
