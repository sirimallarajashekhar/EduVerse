using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlAcademicStore
{
    public async Task<IReadOnlyList<FacultyAssignment>> FacultyAsync(Guid tenantId,Guid campusId,CancellationToken ct)
    {
        var result=new List<FacultyAssignment>();
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,subject_id,academic_year_id,faculty_subject_id,is_active
            FROM academic_faculty_assignments WHERE tenant_id=@tenant AND campus_id=@campus
            ORDER BY id LIMIT 500
            """,db);
        Args(cmd,("@tenant",tenantId.ToString("D")),("@campus",campusId.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            result.Add(new FacultyAssignment(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetGuid(3),reader.GetGuid(4),Guid.Parse(reader.GetString(5)),reader.GetBoolean(6)));
        return result;
    }

    public async Task<FacultyAssignment?> AssignFacultyAsync(Guid tenantId,FacultyAssignmentCreate request,CancellationToken ct)
    {
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await IsActiveCampusAsync(db,tx,tenantId,request.CampusId,ct) ||
           !await HasYearAsync(db,tx,tenantId,request.CampusId,request.AcademicYearId,ct))return null;
        await using (var check=new MySqlCommand("""
            SELECT EXISTS(
              SELECT 1 FROM academic_catalog s
              JOIN tenant_memberships m ON m.tenant_id=s.tenant_id
                AND m.subject_id=@faculty AND m.is_active=1
              JOIN identity_users u ON u.id=@faculty AND u.is_active=1
              WHERE s.id=@subject AND s.tenant_id=@tenant AND s.campus_id=@campus
                AND s.kind='SUBJECT' AND s.is_active=1)
            """,db,tx))
        {
            Args(check,("@faculty",request.FacultyUserId.ToString("D")),
                ("@subject",request.SubjectId.ToString("D")),("@tenant",tenantId.ToString("D")),
                ("@campus",request.CampusId.ToString("D")));
            if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)return null;
        }
        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO academic_faculty_assignments
            (id,tenant_id,campus_id,subject_id,academic_year_id,faculty_subject_id)
            VALUES (@id,@tenant,@campus,@subject,@year,@faculty)
            """,db,tx);
        Args(cmd,("@id",id.ToString("D")),("@tenant",tenantId.ToString("D")),
            ("@campus",request.CampusId.ToString("D")),("@subject",request.SubjectId.ToString("D")),
            ("@year",request.AcademicYearId.ToString("D")),("@faculty",request.FacultyUserId.ToString("D")));
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new FacultyAssignment(id,tenantId,request.CampusId,request.SubjectId,
            request.AcademicYearId,request.FacultyUserId,true);
    }

    public async Task<IReadOnlyList<AcademicEvent>> EventsAsync(Guid tenantId,Guid campusId,CancellationToken ct)
    {
        var result=new List<AcademicEvent>();
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,tenant_id,campus_id,academic_year_id,kind,title,starts_on,ends_on,notes
            FROM academic_calendar_events WHERE tenant_id=@tenant AND campus_id=@campus
            ORDER BY starts_on DESC LIMIT 500
            """,db);
        Args(cmd,("@tenant",tenantId.ToString("D")),("@campus",campusId.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            result.Add(new AcademicEvent(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetGuid(3),reader.GetString(4),reader.GetString(5),
                DateOnly.FromDateTime(reader.GetDateTime(6)),DateOnly.FromDateTime(reader.GetDateTime(7)),
                reader.IsDBNull(8)?null:reader.GetString(8)));
        return result;
    }

    public async Task<AcademicEvent?> CreateEventAsync(Guid tenantId,AcademicEventCreate request,CancellationToken ct)
    {
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        if(!await IsActiveCampusAsync(db,tx,tenantId,request.CampusId,ct) ||
           !await HasYearAsync(db,tx,tenantId,request.CampusId,request.AcademicYearId,ct))return null;
        var id=Guid.NewGuid();
        await using var cmd=new MySqlCommand("""
            INSERT INTO academic_calendar_events
              (id,tenant_id,campus_id,academic_year_id,kind,title,starts_on,ends_on,notes)
            VALUES (@id,@tenant,@campus,@year,@kind,@title,@starts,@ends,@notes)
            """,db,tx);
        Args(cmd,("@id",id.ToString("D")),("@tenant",tenantId.ToString("D")),
            ("@campus",request.CampusId.ToString("D")),("@year",request.AcademicYearId.ToString("D")),
            ("@kind",request.Kind),("@title",request.Title),
            ("@starts",request.StartsOn.ToDateTime(TimeOnly.MinValue)),
            ("@ends",request.EndsOn.ToDateTime(TimeOnly.MinValue)),("@notes",request.Notes));
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return new AcademicEvent(id,tenantId,request.CampusId,request.AcademicYearId,
            request.Kind,request.Title,request.StartsOn,request.EndsOn,request.Notes);
    }

    public async Task<AcademicOverview> OverviewAsync(Guid tenantId,Guid campusId,CancellationToken ct)
    {
        await using var db=Connect();
        await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT
             (SELECT COUNT(*) FROM academic_years WHERE tenant_id=@tenant AND campus_id=@campus),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='PROGRAM'),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='GRADE'),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='CLASS'),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='SECTION'),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='COURSE'),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='SUBJECT'),
             (SELECT COUNT(*) FROM academic_catalog WHERE tenant_id=@tenant AND campus_id=@campus AND kind='BATCH'),
             (SELECT COUNT(*) FROM academic_faculty_assignments WHERE tenant_id=@tenant AND campus_id=@campus),
             (SELECT COUNT(*) FROM academic_calendar_events WHERE tenant_id=@tenant AND campus_id=@campus)
            """,db);
        Args(cmd,("@tenant",tenantId.ToString("D")),("@campus",campusId.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct))throw new InvalidOperationException("Overview missing.");
        return new AcademicOverview(
            Convert.ToInt32(reader.GetInt64(0)),Convert.ToInt32(reader.GetInt64(1)),
            Convert.ToInt32(reader.GetInt64(2)),Convert.ToInt32(reader.GetInt64(3)),
            Convert.ToInt32(reader.GetInt64(4)),Convert.ToInt32(reader.GetInt64(5)),
            Convert.ToInt32(reader.GetInt64(6)),Convert.ToInt32(reader.GetInt64(7)),
            Convert.ToInt32(reader.GetInt64(8)),Convert.ToInt32(reader.GetInt64(9)));
    }
}
