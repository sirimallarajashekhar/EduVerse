using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlStudentStore
{
    public async Task<GuardianRecord?> AddGuardianAsync(Guid tenant,Guid campus,Guid student,GuardianCreate request,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await ActiveStudent(db,tx,tenant,campus,student,ct))return null;
        var id=Guid.NewGuid();
        if(request.IsPrimary)
        {
            await using var clear=new MySqlCommand("""
                UPDATE student_guardians SET is_primary=0
                WHERE tenant_id=@tenant AND campus_id=@campus AND student_id=@student
                """,db,tx);
            Add(clear,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
            await clear.ExecuteNonQueryAsync(ct);
        }
        await using var cmd=new MySqlCommand("""
            INSERT INTO student_guardians(id,tenant_id,campus_id,student_id,full_name,
              relationship_code,contact_phone,contact_email,is_primary)
            VALUES(@id,@tenant,@campus,@student,@name,@relation,@phone,@email,@primary)
            """,db,tx);
        Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)),
            ("@name",request.FullName),("@relation",request.Relationship),("@phone",request.Phone),
            ("@email",request.Email),("@primary",request.IsPrimary));
        await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        return new GuardianRecord(id,student,request.FullName,request.Relationship,request.Phone,request.Email,request.IsPrimary);
    }
    public async Task<IReadOnlyList<GuardianRecord>> GuardiansAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct)
    {
        var list=new List<GuardianRecord>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT g.id,g.student_id,g.full_name,g.relationship_code,g.contact_phone,g.contact_email,g.is_primary
            FROM student_guardians g JOIN students s ON s.id=g.student_id AND s.tenant_id=g.tenant_id AND s.campus_id=g.campus_id
            WHERE g.tenant_id=@tenant AND g.campus_id=@campus AND g.student_id=@student
            ORDER BY g.is_primary DESC,g.full_name LIMIT 20
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))
            list.Add(new GuardianRecord(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),
                r.IsDBNull(4)?null:r.GetString(4),r.IsDBNull(5)?null:r.GetString(5),r.GetBoolean(6)));
        return list;
    }
    private static async Task<bool> ValidPlacement(MySqlConnection db,MySqlTransaction tx,
        Guid tenant,Guid campus,EnrollmentCreate input,CancellationToken ct)
    {
        await using var cmd=new MySqlCommand("""
            SELECT EXISTS(
              SELECT 1 FROM academic_catalog a
              JOIN academic_years y ON y.tenant_id=a.tenant_id AND y.campus_id=a.campus_id
                AND y.id=@year AND y.is_active=1
              WHERE a.id=@item AND a.tenant_id=@tenant AND a.campus_id=@campus
                AND a.is_active=1 AND a.kind IN ('SECTION','CLASS','GRADE','COURSE','BATCH'))
            """,db,tx);
        Add(cmd,("@item",Id(input.AcademicItemId)),("@year",Id(input.AcademicYearId)),
            ("@tenant",Id(tenant)),("@campus",Id(campus)));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    public async Task<EnrollmentRecord?> EnrollAsync(Guid tenant,Guid campus,Guid student,EnrollmentCreate input,CancellationToken ct)
    {
        await using var db=Connect();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await ActiveStudent(db,tx,tenant,campus,student,ct)||
           !await ValidPlacement(db,tx,tenant,campus,input,ct))return null;
        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO student_enrollments
             (id,tenant_id,campus_id,student_id,academic_year_id,academic_item_id)
            VALUES(@id,@tenant,@campus,@student,@year,@item)
            """,db,tx);
        Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(campus)),
            ("@student",Id(student)),("@year",Id(input.AcademicYearId)),("@item",Id(input.AcademicItemId)));
        await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
        return new EnrollmentRecord(id,student,input.AcademicYearId,input.AcademicItemId,"ACTIVE");
    }
    public async Task<IReadOnlyList<EnrollmentRecord>> EnrollmentsAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct)
    {
        var list=new List<EnrollmentRecord>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT e.id,e.student_id,e.academic_year_id,e.academic_item_id,e.status
            FROM student_enrollments e
            JOIN students s ON s.id=e.student_id AND s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id
            WHERE e.tenant_id=@tenant AND e.campus_id=@campus AND e.student_id=@student
            ORDER BY e.created_utc DESC LIMIT 50
            """,db);
        Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@student",Id(student)));
        await using var r=await cmd.ExecuteReaderAsync(ct);
        while(await r.ReadAsync(ct))
            list.Add(new EnrollmentRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetString(4)));
        return list;
    }
}
