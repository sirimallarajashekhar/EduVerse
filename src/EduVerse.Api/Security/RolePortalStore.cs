using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using MySqlConnector;

namespace EduVerse.Api.Security;

public sealed record PortalAttendance(DateOnly Date,string Status,string Scope);
public sealed record PortalResult(Guid ExamId,string Exam,decimal Total,decimal Maximum,decimal Percentage,decimal Gpa,bool Passed);
public sealed record PortalTimetable(int DayOfWeek,string Class,string Subject,TimeOnly StartsAt,TimeOnly EndsAt);
public sealed record PortalAssignment(Guid Id,string Title,string Instructions,DateTime DueUtc,decimal MaximumScore);
public sealed record PortalNotice(string Title,string Body,DateTime PublishedUtc);
public sealed record PortalRoster(Guid StudentId,Guid EnrollmentId,string AdmissionNumber,string GivenName,string FamilyName);
public sealed record PortalLesson(Guid Id,string Title,string Body,string? ResourceUrl,string Status);

/// <summary>Self-service SQL projections; every query uses verified link + current JWT subject.</summary>
public sealed class RolePortalStore(string connectionString,ITenantContext context,IIdentityStore identities)
{
    private MySqlConnection Open()=>new(connectionString);
    private static void Add(MySqlCommand cmd,params (string Key,object? Value)[] pairs)
    {foreach(var (key,value) in pairs)cmd.Parameters.AddWithValue(key,value??DBNull.Value);}
    private async Task<(Guid Tenant,string Subject)> Actor(CancellationToken ct)
    {
        if(!context.TryGet(out var actor)||actor is null||!Guid.TryParse(actor.SubjectId,out _))
            throw new UnauthorizedAccessException();
        return (actor.TenantId,actor.SubjectId);
    }
    private async Task<bool> Owns(Guid tenant,string subject,Guid campus,Guid student,string relation,MySqlConnection db,CancellationToken ct)
    {
        await using var check=new MySqlCommand("""
            SELECT EXISTS (SELECT 1 FROM student_portal_links l
            JOIN students s ON s.tenant_id=l.tenant_id AND s.campus_id=l.campus_id
              AND s.id=l.student_id AND s.status='ACTIVE'
            JOIN campuses c ON c.tenant_id=l.tenant_id AND c.id=l.campus_id AND c.is_active=1
            JOIN tenant_memberships m ON m.tenant_id=l.tenant_id AND m.subject_id=l.subject_id
              AND m.is_active=1
            JOIN identity_users u ON u.id=l.subject_id AND u.is_active=1
            WHERE l.tenant_id=@tenant AND l.subject_id=@subject AND l.campus_id=@campus
              AND l.student_id=@student AND l.relationship_code=@relation)
            """,db);
        Add(check,("@tenant",tenant.ToString("D")),("@subject",subject),
            ("@campus",campus.ToString("D")),("@student",student.ToString("D")),("@relation",relation));
        return Convert.ToInt32(await check.ExecuteScalarAsync(ct))==1;
    }
    private async Task<MySqlConnection?> OpenOwned(Guid campus,Guid student,string relation,CancellationToken ct)
    {
        var (tenant,subject)=await Actor(ct);
        if(campus==Guid.Empty||student==Guid.Empty)return null;
        var db=Open();
        try
        {
            await db.OpenAsync(ct);
            if(await Owns(tenant,subject,campus,student,relation,db,ct))return db;
            await db.DisposeAsync();return null;
        }
        catch {await db.DisposeAsync();throw;}
    }
    public async Task<IReadOnlyList<PortalAttendance>?> Attendance(Guid campus,Guid student,string relation,CancellationToken ct)
    {
        await using var db=await OpenOwned(campus,student,relation,ct);
        if(db is null)return null;
        var actor=await Actor(ct);var list=new List<PortalAttendance>();
        await using var cmd=new MySqlCommand("""
            SELECT a.attendance_date,a.status,a.attendance_scope FROM student_attendance a
            JOIN student_enrollments e ON e.tenant_id=a.tenant_id AND e.campus_id=a.campus_id
             AND e.id=a.enrollment_id AND e.student_id=a.student_id
            WHERE a.tenant_id=@tenant AND a.campus_id=@campus AND a.student_id=@student
            ORDER BY a.attendance_date DESC LIMIT 120
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@student",student.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PortalAttendance(DateOnly.FromDateTime(reader.GetDateTime(0)),
                reader.GetString(1),reader.GetString(2)));
        return list;
    }
    public async Task<IReadOnlyList<PortalResult>?> Results(Guid campus,Guid student,string relation,CancellationToken ct)
    {
        await using var db=await OpenOwned(campus,student,relation,ct);
        if(db is null)return null;
        var actor=await Actor(ct);var list=new List<PortalResult>();
        await using var cmd=new MySqlCommand("""
            SELECT p.exam_id,x.title,p.total_marks,p.maximum_marks,p.percentage,p.gpa,p.passed
            FROM exam_result_publications p
            JOIN student_enrollments e ON e.tenant_id=p.tenant_id AND e.campus_id=p.campus_id
              AND e.id=p.enrollment_id AND e.student_id=@student
            JOIN examinations x ON x.tenant_id=p.tenant_id AND x.campus_id=p.campus_id
              AND x.id=p.exam_id AND x.status='PUBLISHED'
            WHERE p.tenant_id=@tenant AND p.campus_id=@campus
            ORDER BY p.published_utc DESC LIMIT 100
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@student",student.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PortalResult(reader.GetGuid(0),reader.GetString(1),reader.GetDecimal(2),
                reader.GetDecimal(3),reader.GetDecimal(4),reader.GetDecimal(5),reader.GetBoolean(6)));
        return list;
    }
    public async Task<IReadOnlyList<PortalTimetable>?> Timetable(Guid campus,Guid student,CancellationToken ct)
    {
        await using var db=await OpenOwned(campus,student,"SELF",ct);
        if(db is null)return null;
        var actor=await Actor(ct);var list=new List<PortalTimetable>();
        await using var cmd=new MySqlCommand("""
            SELECT t.day_of_week,cls.display_name,sub.display_name,p.starts_at,p.ends_at
            FROM student_enrollments e
            JOIN academic_timetable_slots t ON t.tenant_id=e.tenant_id AND t.campus_id=e.campus_id
              AND t.class_item_id=e.academic_item_id AND t.academic_year_id=e.academic_year_id
              AND t.is_active=1
            JOIN academic_catalog cls ON cls.tenant_id=t.tenant_id AND cls.campus_id=t.campus_id
              AND cls.id=t.class_item_id
            JOIN academic_catalog sub ON sub.tenant_id=t.tenant_id AND sub.campus_id=t.campus_id
              AND sub.id=t.subject_id
            JOIN academic_periods p ON p.tenant_id=t.tenant_id AND p.campus_id=t.campus_id
              AND p.id=t.period_id AND p.is_active=1
            WHERE e.tenant_id=@tenant AND e.campus_id=@campus AND e.student_id=@student
              AND e.status='ACTIVE'
            ORDER BY t.day_of_week,p.starts_at LIMIT 200
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@student",student.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PortalTimetable(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),
                TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),TimeOnly.FromTimeSpan(reader.GetTimeSpan(4))));
        return list;
    }
    public async Task<IReadOnlyList<PortalAssignment>?> Assignments(Guid campus,Guid student,CancellationToken ct)
    {
        await using var db=await OpenOwned(campus,student,"SELF",ct);
        if(db is null)return null;
        var actor=await Actor(ct);var list=new List<PortalAssignment>();
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT a.id,a.title,a.instructions,a.due_utc,a.max_score
            FROM student_enrollments e
            JOIN learning_lessons lesson ON lesson.tenant_id=e.tenant_id AND lesson.campus_id=e.campus_id
              AND lesson.class_id=e.academic_item_id AND lesson.academic_year_id=e.academic_year_id
              AND lesson.status='PUBLISHED'
            JOIN learning_assignments a ON a.tenant_id=lesson.tenant_id
              AND a.campus_id=lesson.campus_id AND a.lesson_id=lesson.id
              AND a.status='PUBLISHED'
            WHERE e.tenant_id=@tenant AND e.campus_id=@campus AND e.student_id=@student
              AND e.status='ACTIVE'
            ORDER BY a.due_utc DESC LIMIT 100
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@student",student.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PortalAssignment(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),
                reader.GetDateTime(3),reader.GetDecimal(4)));
        return list;
    }
    public async Task<IReadOnlyList<PortalNotice>?> Notices(Guid campus,Guid student,string relation,CancellationToken ct)
    {
        await using var db=await OpenOwned(campus,student,relation,ct);
        if(db is null)return null;
        var actor=await Actor(ct);var list=new List<PortalNotice>();
        await using var cmd=new MySqlCommand("""
            SELECT title,body,published_utc FROM learning_announcements
            WHERE tenant_id=@tenant AND campus_id=@campus AND
              (audience='ALL' OR (@relation='PARENT' AND audience='GUARDIANS'))
            ORDER BY published_utc DESC LIMIT 100
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@relation",relation));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PortalNotice(reader.GetString(0),reader.GetString(1),reader.GetDateTime(2)));
        return list;
    }
    public async Task<IReadOnlyList<PortalRoster>> TeachingRoster(Guid campus,Guid classId,CancellationToken ct)
    {
        var actor=await Actor(ct);
        var user=new EduVerse.Domain.TenantIdentity(actor.Tenant,actor.Subject);
        if(campus==Guid.Empty||classId==Guid.Empty||
           !await identities.HasPermissionAsync(user,"students.read",campus,ct))
            throw new UnauthorizedAccessException();
        var items=new List<PortalRoster>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT s.id,e.id,s.admission_number,s.given_name,s.family_name
            FROM student_enrollments e
            JOIN students s ON s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id
             AND s.id=e.student_id AND s.status='ACTIVE'
            JOIN academic_timetable_slots t ON t.tenant_id=e.tenant_id AND t.campus_id=e.campus_id
             AND t.class_item_id=e.academic_item_id AND t.academic_year_id=e.academic_year_id
             AND t.teacher_subject_id=@subject AND t.is_active=1
            JOIN campuses c ON c.id=e.campus_id AND c.tenant_id=e.tenant_id AND c.is_active=1
            WHERE e.tenant_id=@tenant AND e.campus_id=@campus AND e.academic_item_id=@class
             AND e.status='ACTIVE'
            ORDER BY s.admission_number LIMIT 500
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@class",classId.ToString("D")),("@subject",actor.Subject));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            items.Add(new PortalRoster(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4)));
        return items;
    }

    public async Task<IReadOnlyList<PortalLesson>> TeachingLessons(Guid campus,CancellationToken ct)
    {
        var actor=await Actor(ct);
        var identity=new EduVerse.Domain.TenantIdentity(actor.Tenant,actor.Subject);
        if(campus==Guid.Empty || !await identities.HasPermissionAsync(identity,"learning.read",campus,ct))
            throw new UnauthorizedAccessException();
        var list=new List<PortalLesson>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT l.id,l.title,l.body,l.resource_url,l.status
            FROM learning_lessons l
            JOIN academic_timetable_slots slot ON slot.tenant_id=l.tenant_id
             AND slot.campus_id=l.campus_id AND slot.class_item_id=l.class_id
             AND slot.academic_year_id=l.academic_year_id AND slot.subject_id=l.subject_id
             AND slot.teacher_subject_id=@subject AND slot.is_active=1
            WHERE l.tenant_id=@tenant AND l.campus_id=@campus
            ORDER BY l.title LIMIT 100
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),("@subject",actor.Subject));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            list.Add(new PortalLesson(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),
                reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(4)));
        return list;
    }
}
