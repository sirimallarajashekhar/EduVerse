using EduVerse.Application.Administration;
using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Api.Security;

public sealed record ActivityChoice(Guid Id,string Label,Guid? CampusId=null,Guid? ClassId=null,
    Guid? YearId=null,Guid? SubjectId=null,string? ClassName=null,string? YearName=null,string? SubjectName=null,string? Relation=null);

/// <summary>Bounded read-only choices. IDs are opaque values; live membership, permissions,
/// subscription, campus and assigned-class/verified-link restrictions are API-owned.</summary>
public sealed class ActivityLookupStore(string connectionString,ITenantContext context,
    IIdentityStore identities,IInstitutionAdminStore subscriptions,TenantReadAuthorizer authorizer)
{
    public static readonly IReadOnlySet<string> Resources=new HashSet<string>(StringComparer.Ordinal)
    {"campuses","linked-students","teacher-contexts","teacher-assessments","teacher-periods","teacher-lessons","teacher-submissions","employees","users","purchases"};
    private TenantIdentity Actor()=>context.TryGet(out var actor)&&actor is not null?actor:throw new UnauthorizedAccessException();
    public async Task<IReadOnlyList<ActivityChoice>> Choices(string resource,Guid? campus,string? search,CancellationToken ct)
    {
        if(!Resources.Contains(resource))throw new ArgumentException("Unknown selection list.");
        var actor=Actor();var admin=await identities.IsAdminAsync(actor,ct);
        await using var db=new MySqlConnection(connectionString);await db.OpenAsync(ct);
        const string live="""
            EXISTS(SELECT 1 FROM tenant_memberships m JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
            JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
            WHERE m.tenant_id=@t AND m.subject_id=@u AND m.is_active=1)
            """;
        string sql;
        if(resource=="campuses")
        {
            sql="SELECT c.id,c.display_name AS label,c.id AS campus_id FROM campuses c WHERE c.tenant_id=@t AND c.is_active=1 AND "+live;
            var candidates=await Query(db,sql,actor,campus,search,ct);
            var links=await Query(db,LinkedSql+" AND "+live,actor,null,null,ct);
            var allowed=new List<ActivityChoice>();
            foreach(var choice in candidates)
                if(admin||links.Any(x=>x.CampusId==choice.Id)||await identities.HasPermissionAsync(actor,"academic.read",choice.Id,ct)||await identities.HasPermissionAsync(actor,"learning.read",choice.Id,ct)||await identities.HasPermissionAsync(actor,"attendance.read",choice.Id,ct)||await identities.HasPermissionAsync(actor,"exams.read",choice.Id,ct))allowed.Add(choice);
            return allowed;
        }
        if(resource=="linked-students")return await Query(db,LinkedSql+" AND "+live,actor,campus,search,ct);
        if(campus is not Guid c||c==Guid.Empty)throw new ArgumentException("Choose a campus.");
        if(resource is "employees" or "users" or "purchases")
        {
            if(!admin)throw new UnauthorizedAccessException();
            var feature=resource=="employees"?"hrms":resource=="purchases"?"ams":"core";
            if(!(await subscriptions.SubscriptionAsync(actor.TenantId,ct)).Features.Contains(feature))throw new UnauthorizedAccessException();
            // Match the active campus as well as tenant; user accounts remain tenant-scoped.
            await using var check=new MySqlCommand("SELECT COUNT(*) FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1",db);
            check.Parameters.AddWithValue("@t",actor.TenantId.ToString("D"));check.Parameters.AddWithValue("@c",c.ToString("D"));
            if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)throw new UnauthorizedAccessException();
            sql=resource switch
            {
                "employees"=>"SELECT id,CONCAT(given_name,' ',family_name,' · ',employee_number) AS label,campus_id FROM hr_employees WHERE tenant_id=@t AND campus_id=@c AND status<>'EXITED'",
                "users"=>"SELECT u.id,CONCAT(u.display_name,' · ',u.email) AS label FROM identity_users u JOIN tenant_memberships m ON m.subject_id=u.id WHERE m.tenant_id=@t AND m.is_active=1 AND m.role_code='MEMBER' AND u.is_active=1",
                _=>"SELECT p.id,CONCAT('Ordered purchase · ',s.display_name,' · ',p.ordered_quantity,' × ',p.unit_price) AS label,p.campus_id FROM ams_purchase_orders p JOIN ams_stock_items s ON s.tenant_id=p.tenant_id AND s.campus_id=p.campus_id AND s.id=p.stock_item_id WHERE p.tenant_id=@t AND p.campus_id=@c AND p.status='ORDERED'"
            };
            return await Query(db,sql+" AND "+live,actor,campus,search,ct);
        }
        // Tenant administrators may have tenant-wide permission, but a supplied campus must still belong to them.
        await using(var campusCheck=new MySqlCommand("SELECT COUNT(*) FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1",db))
        {
            campusCheck.Parameters.AddWithValue("@t",actor.TenantId.ToString("D"));campusCheck.Parameters.AddWithValue("@c",c.ToString("D"));
            if(Convert.ToInt32(await campusCheck.ExecuteScalarAsync(ct))!=1)throw new UnauthorizedAccessException();
        }
        var permission=resource=="teacher-assessments"?"exams.read":resource=="teacher-periods"?"attendance.read":resource is "teacher-lessons" or "teacher-submissions"?"learning.read":"academic.read";
        await authorizer.RequireAsync(permission,c,ct);
        const string slots="""
            FROM academic_timetable_slots slot
            JOIN academic_catalog cls ON cls.tenant_id=slot.tenant_id AND cls.campus_id=slot.campus_id AND cls.id=slot.class_item_id AND cls.is_active=1
            JOIN academic_catalog sub ON sub.tenant_id=slot.tenant_id AND sub.campus_id=slot.campus_id AND sub.id=slot.subject_id AND sub.is_active=1
            JOIN academic_years y ON y.tenant_id=slot.tenant_id AND y.campus_id=slot.campus_id AND y.id=slot.academic_year_id AND y.is_active=1
            """;
        const string scope="slot.tenant_id=@t AND slot.campus_id=@c AND slot.teacher_subject_id=@u AND slot.is_active=1";
        sql=resource switch
        {
            "teacher-contexts"=>"SELECT DISTINCT slot.id,CONCAT(cls.display_name,' · ',y.display_name,' · ',sub.display_name) AS label,slot.campus_id,slot.class_item_id AS class_id,slot.academic_year_id AS year_id,slot.subject_id,cls.display_name AS class_name,y.display_name AS year_name,sub.display_name AS subject_name "+slots+" WHERE "+scope,
            "teacher-periods"=>"SELECT DISTINCT p.id,p.display_name AS label,p.campus_id,slot.class_item_id AS class_id,slot.academic_year_id AS year_id "+slots+" JOIN academic_periods p ON p.tenant_id=slot.tenant_id AND p.campus_id=slot.campus_id AND p.id=slot.period_id AND p.is_active=1 WHERE "+scope,
            "teacher-assessments"=>"SELECT DISTINCT a.id,CONCAT(x.title,' · ',cls.display_name,' · ',sub.display_name) AS label,a.campus_id,a.academic_item_id AS class_id,x.academic_year_id AS year_id "+slots+" JOIN examinations x ON x.tenant_id=slot.tenant_id AND x.campus_id=slot.campus_id AND x.academic_year_id=slot.academic_year_id AND x.status='DRAFT' JOIN exam_assessments a ON a.tenant_id=x.tenant_id AND a.campus_id=x.campus_id AND a.exam_id=x.id AND a.academic_item_id=slot.class_item_id AND a.subject_id=slot.subject_id WHERE "+scope,
            "teacher-lessons"=>"SELECT DISTINCT l.id,CONCAT(l.title,' · ',cls.display_name,' · ',sub.display_name) AS label,l.campus_id "+slots+" JOIN learning_lessons l ON l.tenant_id=slot.tenant_id AND l.campus_id=slot.campus_id AND l.class_id=slot.class_item_id AND l.subject_id=slot.subject_id AND l.academic_year_id=slot.academic_year_id AND l.status='PUBLISHED' WHERE "+scope,
            _=>"SELECT DISTINCT work.id,CONCAT(s.given_name,' ',s.family_name,' · ',a.title) AS label,work.campus_id "+slots+" JOIN learning_lessons l ON l.tenant_id=slot.tenant_id AND l.campus_id=slot.campus_id AND l.class_id=slot.class_item_id AND l.subject_id=slot.subject_id AND l.academic_year_id=slot.academic_year_id JOIN learning_assignments a ON a.tenant_id=l.tenant_id AND a.campus_id=l.campus_id AND a.lesson_id=l.id JOIN learning_submissions work ON work.tenant_id=a.tenant_id AND work.campus_id=a.campus_id AND work.assignment_id=a.id AND work.status='SUBMITTED' JOIN student_enrollments e ON e.tenant_id=work.tenant_id AND e.campus_id=work.campus_id AND e.id=work.enrollment_id JOIN students s ON s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id AND s.id=e.student_id WHERE "+scope
        };
        return await Query(db,sql+" AND "+live,actor,campus,search,ct);
    }
    private const string LinkedSql="""
        SELECT s.id,CONCAT(s.given_name,' ',s.family_name,' · ',s.admission_number,' · ',c.display_name) AS label,
          l.campus_id,l.relationship_code AS relation
        FROM student_portal_links l JOIN students s ON s.tenant_id=l.tenant_id AND s.campus_id=l.campus_id AND s.id=l.student_id AND s.status='ACTIVE'
        JOIN campuses c ON c.tenant_id=l.tenant_id AND c.id=l.campus_id AND c.is_active=1
        WHERE l.tenant_id=@t AND l.subject_id=@u AND (@c IS NULL OR l.campus_id=@c)
        """;
    private static async Task<IReadOnlyList<ActivityChoice>> Query(MySqlConnection db,string sql,TenantIdentity actor,Guid? campus,string? search,CancellationToken ct)
    {
        var query=(search??string.Empty).Trim();if(query.Length>80)throw new ArgumentException("Search is too long.");
        await using var cmd=new MySqlCommand("SELECT * FROM ("+sql+") choices WHERE (@q='' OR label LIKE CONCAT('%',@q,'%')) ORDER BY label,id LIMIT 500",db);
        cmd.Parameters.AddWithValue("@t",actor.TenantId.ToString("D"));cmd.Parameters.AddWithValue("@u",actor.SubjectId);
        cmd.Parameters.AddWithValue("@c",campus?.ToString("D"));cmd.Parameters.AddWithValue("@q",query);
        var result=new List<ActivityChoice>();await using var reader=await cmd.ExecuteReaderAsync(ct);
        var columns=Enumerable.Range(0,reader.FieldCount).ToDictionary(reader.GetName,StringComparer.OrdinalIgnoreCase);
        Guid? GuidAt(string name)=>columns.TryGetValue(name,out var i)&&!reader.IsDBNull(i)?reader.GetGuid(i):null;
        string? TextAt(string name)=>columns.TryGetValue(name,out var i)&&!reader.IsDBNull(i)?reader.GetString(i):null;
        while(await reader.ReadAsync(ct))result.Add(new ActivityChoice(reader.GetGuid(reader.GetOrdinal("id")),reader.GetString(reader.GetOrdinal("label")),GuidAt("campus_id"),GuidAt("class_id"),GuidAt("year_id"),GuidAt("subject_id"),TextAt("class_name"),TextAt("year_name"),TextAt("subject_name"),TextAt("relation")));
        return result;
    }
}
