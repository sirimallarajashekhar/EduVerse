using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using MySqlConnector;

namespace EduVerse.Api.Security;

public sealed record StudentPortalLinkRequest(Guid CampusId,Guid StudentId,Guid UserId,string Relationship);
public sealed record OwnedStudent(Guid Id,Guid CampusId,string AdmissionNumber,string GivenName,string FamilyName,string Status);
public sealed record AssignedClass(Guid ClassId,Guid CampusId,Guid AcademicYearId);
public sealed record LinkedStudent(Guid StudentId,Guid CampusId,string Relationship);

/// <summary>
/// Subject always comes from validated tenant JWT; all record lookups are scoped in SQL.
/// Portal linking is a privileged, audited administrative assertion.
/// </summary>
public sealed class StudentOwnershipStore(string connectionString,IIdentityStore identity,ITenantContext tenant)
{
    private MySqlConnection Open()=>new(connectionString);
    private static void Add(MySqlCommand command,params (string Name,object? Value)[] items)
    {foreach(var (name,value) in items)command.Parameters.AddWithValue(name,value??DBNull.Value);}
    private async Task<(Guid Tenant,string Subject)> Actor(CancellationToken ct)
    {
        if(!tenant.TryGet(out var actor)||actor is null||!Guid.TryParse(actor.SubjectId,out _))
            throw new UnauthorizedAccessException();
        return (actor.TenantId,actor.SubjectId);
    }

    public async Task<bool> LinkAsync(StudentPortalLinkRequest req,CancellationToken ct)
    {
        var actor=await Actor(ct);
        if(!await identity.IsAdminAsync(new EduVerse.Domain.TenantIdentity(actor.Tenant,actor.Subject),ct))
            throw new UnauthorizedAccessException();
        if(req.CampusId==Guid.Empty||req.StudentId==Guid.Empty||req.UserId==Guid.Empty||
           req.Relationship is not ("SELF" or "PARENT"))throw new ArgumentException("Invalid portal link.");

        await using var db=Open();await db.OpenAsync(ct);
        await using var tx=await db.BeginTransactionAsync(ct);
        await using(var validate=new MySqlCommand("""
            SELECT EXISTS(
             SELECT 1 FROM students s JOIN campuses c ON c.tenant_id=s.tenant_id AND c.id=s.campus_id
             JOIN tenant_memberships m ON m.tenant_id=s.tenant_id AND m.subject_id=@user AND m.is_active=1
             JOIN identity_users u ON u.id=@user AND u.is_active=1
             WHERE s.tenant_id=@tenant AND s.campus_id=@campus AND s.id=@student
               AND s.status='ACTIVE' AND c.is_active=1
               AND (@relationship='SELF' OR EXISTS(
                 SELECT 1 FROM student_guardians g
                 WHERE g.tenant_id=s.tenant_id AND g.campus_id=s.campus_id AND g.student_id=s.id
                   AND g.contact_email IS NOT NULL
                   AND UPPER(TRIM(g.contact_email))=u.email_normalized))
            )
            """,db,tx))
        {
            Add(validate,("@tenant",actor.Tenant.ToString("D")),("@campus",req.CampusId.ToString("D")),
                ("@student",req.StudentId.ToString("D")),("@user",req.UserId.ToString("D")),
                ("@relationship",req.Relationship));
            if(Convert.ToInt32(await validate.ExecuteScalarAsync(ct))!=1)
            {await tx.RollbackAsync(ct);return false;}
        }
        await using(var cmd=new MySqlCommand("""
            INSERT INTO student_portal_links(tenant_id,campus_id,student_id,subject_id,relationship_code,linked_by)
            VALUES(@tenant,@campus,@student,@user,@relationship,@by)
            ON DUPLICATE KEY UPDATE relationship_code=VALUES(relationship_code),linked_by=VALUES(linked_by),
                                    linked_utc=UTC_TIMESTAMP(6)
            """,db,tx))
        {
            Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",req.CampusId.ToString("D")),
                ("@student",req.StudentId.ToString("D")),("@user",req.UserId.ToString("D")),
                ("@relationship",req.Relationship),("@by",actor.Subject));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await using(var audit=new MySqlCommand("""
            INSERT INTO identity_audit_events(tenant_id,user_id,event_code)
            VALUES(@tenant,@actor,'PORTAL_STUDENT_LINKED')
            """,db,tx))
        {
            Add(audit,("@tenant",actor.Tenant.ToString("D")),("@actor",actor.Subject));
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<LinkedStudent>> MyLinksAsync(CancellationToken ct)
    {
        var actor=await Actor(ct);
        var links=new List<LinkedStudent>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT l.student_id,l.campus_id,l.relationship_code
            FROM student_portal_links l
            JOIN students s ON s.tenant_id=l.tenant_id AND s.campus_id=l.campus_id AND s.id=l.student_id
            JOIN campuses c ON c.tenant_id=l.tenant_id AND c.id=l.campus_id
            JOIN tenant_memberships m ON m.tenant_id=l.tenant_id AND m.subject_id=l.subject_id
            WHERE l.tenant_id=@tenant AND l.subject_id=@subject AND m.is_active=1
              AND c.is_active=1 AND s.status='ACTIVE'
            ORDER BY l.campus_id,l.student_id LIMIT 200
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@subject",actor.Subject));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            links.Add(new LinkedStudent(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2)));
        return links;
    }

    public async Task<OwnedStudent?> MineAsync(Guid campus,Guid student,CancellationToken ct)
    {
        var actor=await Actor(ct);
        if(campus==Guid.Empty||student==Guid.Empty)return null;
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT s.id,s.campus_id,s.admission_number,s.given_name,s.family_name,s.status
            FROM students s
            JOIN campuses c ON c.tenant_id=s.tenant_id AND c.id=s.campus_id AND c.is_active=1
            JOIN student_portal_links l ON l.tenant_id=s.tenant_id AND l.campus_id=s.campus_id
                 AND l.student_id=s.id AND l.subject_id=@subject
            JOIN tenant_memberships m ON m.tenant_id=l.tenant_id AND m.subject_id=l.subject_id AND m.is_active=1
            JOIN identity_users u ON u.id=@subject AND u.is_active=1
            WHERE s.tenant_id=@tenant AND s.campus_id=@campus AND s.id=@student
              AND s.status='ACTIVE' LIMIT 1
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@subject",actor.Subject),
            ("@campus",campus.ToString("D")),("@student",student.ToString("D")));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ?new OwnedStudent(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetString(5))
            :null;
    }

    public async Task<IReadOnlyList<AssignedClass>> ClassesAsync(Guid campus,CancellationToken ct)
    {
        var actor=await Actor(ct);
        var identityActor=new EduVerse.Domain.TenantIdentity(actor.Tenant,actor.Subject);
        if(campus==Guid.Empty||!await identity.HasPermissionAsync(identityActor,"academic.read",campus,ct))
            throw new UnauthorizedAccessException("Campus access required.");
        var items=new List<AssignedClass>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT t.class_item_id,t.campus_id,t.academic_year_id
            FROM academic_timetable_slots t
            JOIN campuses c ON c.tenant_id=t.tenant_id AND c.id=t.campus_id AND c.is_active=1
            WHERE t.tenant_id=@tenant AND t.campus_id=@campus
              AND t.teacher_subject_id=@subject AND t.is_active=1
            ORDER BY t.class_item_id LIMIT 200
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@subject",actor.Subject));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            items.Add(new AssignedClass(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2)));
        return items;
    }

    public async Task<IReadOnlyList<OwnedStudent>> ClassStudentsAsync(Guid campus,Guid classId,CancellationToken ct)
    {
        var actor=await Actor(ct);
        var identityActor=new EduVerse.Domain.TenantIdentity(actor.Tenant,actor.Subject);
        if(campus==Guid.Empty||classId==Guid.Empty||
           !await identity.HasPermissionAsync(identityActor,"students.read",campus,ct))
            throw new UnauthorizedAccessException("Campus access required.");
        var items=new List<OwnedStudent>();
        await using var db=Open();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT s.id,s.campus_id,s.admission_number,s.given_name,s.family_name,s.status
            FROM student_enrollments e
            JOIN students s ON s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id AND s.id=e.student_id
            JOIN academic_timetable_slots t ON t.tenant_id=e.tenant_id AND t.campus_id=e.campus_id
              AND t.class_item_id=e.academic_item_id AND t.academic_year_id=e.academic_year_id
            JOIN campuses c ON c.tenant_id=e.tenant_id AND c.id=e.campus_id AND c.is_active=1
            WHERE e.tenant_id=@tenant AND e.campus_id=@campus AND e.academic_item_id=@class
              AND e.status='ACTIVE' AND s.status='ACTIVE'
              AND t.teacher_subject_id=@subject AND t.is_active=1
            ORDER BY s.id LIMIT 500
            """,db);
        Add(cmd,("@tenant",actor.Tenant.ToString("D")),("@campus",campus.ToString("D")),
            ("@class",classId.ToString("D")),("@subject",actor.Subject));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            items.Add(new OwnedStudent(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
                reader.GetString(3),reader.GetString(4),reader.GetString(5)));
        return items;
    }
}
