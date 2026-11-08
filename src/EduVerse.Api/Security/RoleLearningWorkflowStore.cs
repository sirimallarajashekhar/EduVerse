using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Api.Security;

public sealed record TeacherAssignmentDraft(Guid CampusId,Guid LessonId,string Title,string Instructions,DateTime DueUtc,decimal MaxScore);
public sealed record StudentWork(Guid CampusId,Guid StudentId,Guid AssignmentId,string ResponseText);
public sealed record TeacherGrade(Guid CampusId,Guid SubmissionId,decimal Score,string Feedback);
public sealed record ParentConversation(Guid CampusId,Guid StudentId,string Subject,string Body);
public sealed record ConversationReply(Guid CampusId,Guid ThreadId,string Body);
public sealed record LearningApproval(Guid CampusId,Guid ContentId);
public sealed record WorkflowAssignment(Guid Id,string Title,string Status);
public sealed record WorkflowSubmission(Guid Id,Guid AssignmentId,Guid EnrollmentId,string Status,decimal? Score,string? Feedback);
public sealed record WorkflowThread(Guid Id,Guid StudentId,string Subject,string Status);
public sealed record WorkflowMessage(Guid Id,string Author,string Body,DateTime CreatedUtc);
public sealed record LearningReview(Guid Id,string Type,string Title,string Status);

/// <summary>Every student, teacher and parent mutation is constrained in the same MySQL transaction as its write.</summary>
public sealed class RoleLearningWorkflowStore(string connectionString,ITenantContext tenant,IIdentityStore identity,
    TenantReadAuthorizer authorizer)
{
    private MySqlConnection Connect()=>new(connectionString);
    private static string Id(Guid guid)=>guid.ToString("D");
    private static void Add(MySqlCommand cmd,params (string Name,object? Value)[] args)
    {foreach(var (name,value) in args)cmd.Parameters.AddWithValue(name,value??DBNull.Value);}
    private static async Task<int> Exec(MySqlConnection db,MySqlTransaction tx,string sql,CancellationToken ct,
        params (string Name,object? Value)[] args)
    {
        await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);return await cmd.ExecuteNonQueryAsync(ct);
    }
    private static async Task<bool> Exists(MySqlConnection db,MySqlTransaction tx,string sql,CancellationToken ct,
        params (string Name,object? Value)[] args)
    {
        await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private TenantIdentity Actor()
    {
        if(!tenant.TryGet(out var actor)||actor is null||!Guid.TryParse(actor.SubjectId,out _))
            throw new UnauthorizedAccessException();
        return actor;
    }
    private async Task<TenantIdentity> Admin(CancellationToken ct)
    {
        var actor=Actor();
        if(!await identity.IsAdminAsync(actor,ct))throw new UnauthorizedAccessException();
        return actor;
    }
    private static string Required(string? value,int limit)
    {
        var text=(value??"").Trim();
        if(text.Length is < 2 || text.Length>limit)throw new ArgumentException("Invalid or oversized content.");
        return text;
    }
    private async Task<bool> Owns(MySqlConnection db,MySqlTransaction tx,TenantIdentity actor,
        Guid campus,Guid student,string relationship,CancellationToken ct)=>
        await Exists(db,tx,"""
          SELECT EXISTS(SELECT 1 FROM student_portal_links l
           JOIN students s ON s.tenant_id=l.tenant_id AND s.campus_id=l.campus_id
             AND s.id=l.student_id AND s.status='ACTIVE'
           JOIN campuses c ON c.tenant_id=l.tenant_id AND c.id=l.campus_id AND c.is_active=1
           JOIN tenant_memberships m ON m.tenant_id=l.tenant_id AND m.subject_id=l.subject_id AND m.is_active=1
           JOIN identity_users u ON u.id=l.subject_id AND u.is_active=1
           WHERE l.tenant_id=@t AND l.campus_id=@c AND l.student_id=@student
             AND l.subject_id=@actor AND l.relationship_code=@rel)
          """,ct,("@t",Id(actor.TenantId)),("@c",Id(campus)),("@student",Id(student)),
              ("@actor",actor.SubjectId),("@rel",relationship));

    public async Task<WorkflowAssignment?> DraftAssignment(TeacherAssignmentDraft request,CancellationToken ct)
    {
        var actor=await authorizer.RequireAsync("learning.manage",request.CampusId,ct);
        if(request.LessonId==Guid.Empty||request.DueUtc.Kind!=DateTimeKind.Utc||
           request.DueUtc<=DateTime.UtcNow||request.MaxScore<=0||request.MaxScore>999999.99m||
           decimal.Round(request.MaxScore,2)!=request.MaxScore)throw new ArgumentException("Invalid assignment date or score.");
        var title=Required(request.Title,160);var instructions=Required(request.Instructions,8000);
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(!await Exists(db,tx,"""
          SELECT EXISTS(SELECT 1 FROM learning_lessons l
            JOIN tenant_memberships m ON m.tenant_id=l.tenant_id AND m.subject_id=@actor AND m.is_active=1
            WHERE l.tenant_id=@t AND l.campus_id=@c AND l.id=@lesson AND l.status='PUBLISHED'
            AND (m.role_code='ADMIN' OR EXISTS(
              SELECT 1 FROM academic_timetable_slots slot
              WHERE slot.tenant_id=l.tenant_id AND slot.campus_id=l.campus_id
                AND slot.teacher_subject_id=m.subject_id AND slot.is_active=1
                AND slot.class_item_id=l.class_id AND slot.academic_year_id=l.academic_year_id
                AND slot.subject_id=l.subject_id)))
          """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
                 ("@actor",actor.SubjectId),("@lesson",Id(request.LessonId))))
            return null;
        var id=Guid.NewGuid();
        await Exec(db,tx,"""
          INSERT INTO learning_assignments(id,tenant_id,campus_id,lesson_id,title,instructions,due_utc,max_score)
          VALUES(@id,@t,@c,@lesson,@title,@body,@due,@max)
          """,ct,("@id",Id(id)),("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
           ("@lesson",Id(request.LessonId)),("@title",title),("@body",instructions),
           ("@due",request.DueUtc),("@max",request.MaxScore));
        await tx.CommitAsync(ct);return new WorkflowAssignment(id,title,"DRAFT");
    }

    public async Task<WorkflowSubmission?> Submit(StudentWork request,CancellationToken ct)
    {
        var actor=Actor();
        if(request.CampusId==Guid.Empty||request.StudentId==Guid.Empty||request.AssignmentId==Guid.Empty)
            throw new ArgumentException("Campus, student and assignment are required.");
        var body=Required(request.ResponseText,8000);
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        // Serialize competing submissions on the same assignment row; avoid duplicate and deadline races.
        Guid enrollment;
        await using(var cmd=new MySqlCommand("""
            SELECT e.id FROM learning_assignments a
             JOIN learning_lessons l ON l.tenant_id=a.tenant_id AND l.campus_id=a.campus_id
               AND l.id=a.lesson_id AND l.status='PUBLISHED'
             JOIN student_enrollments e ON e.tenant_id=l.tenant_id AND e.campus_id=l.campus_id
               AND e.academic_year_id=l.academic_year_id AND e.academic_item_id=l.class_id
               AND e.student_id=@student AND e.status='ACTIVE'
             JOIN student_portal_links own ON own.tenant_id=e.tenant_id AND own.campus_id=e.campus_id
               AND own.student_id=e.student_id AND own.subject_id=@actor AND own.relationship_code='SELF'
             JOIN students s ON s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id
               AND s.id=e.student_id AND s.status='ACTIVE'
             JOIN tenant_memberships m ON m.tenant_id=e.tenant_id AND m.subject_id=@actor AND m.is_active=1
             JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
             JOIN campuses c ON c.id=e.campus_id AND c.tenant_id=e.tenant_id AND c.is_active=1
            WHERE a.tenant_id=@t AND a.campus_id=@c AND a.id=@assignment
              AND a.status='PUBLISHED' AND a.due_utc>UTC_TIMESTAMP(6)
            LIMIT 1 FOR UPDATE
            """,db,tx))
        {
            Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
                ("@assignment",Id(request.AssignmentId)),("@student",Id(request.StudentId)),
                ("@actor",actor.SubjectId));
            var result=await cmd.ExecuteScalarAsync(ct);
            if(result is null)return null;
            enrollment=result is Guid guid?guid:Guid.Parse(Convert.ToString(result)!);
        }
        if(await Exists(db,tx,"""
            SELECT EXISTS(SELECT 1 FROM learning_submissions WHERE tenant_id=@t AND campus_id=@c
              AND assignment_id=@assignment AND enrollment_id=@enrollment)
            """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
             ("@assignment",Id(request.AssignmentId)),("@enrollment",Id(enrollment))))
            throw new InvalidOperationException("Assignment was already submitted.");
        var id=Guid.NewGuid();
        await Exec(db,tx,"""
            INSERT INTO learning_submissions(id,tenant_id,campus_id,assignment_id,enrollment_id,response_text)
            VALUES(@id,@t,@c,@assignment,@enrollment,@body)
            """,ct,("@id",Id(id)),("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
             ("@assignment",Id(request.AssignmentId)),("@enrollment",Id(enrollment)),("@body",body));
        await tx.CommitAsync(ct);
        return new WorkflowSubmission(id,request.AssignmentId,enrollment,"SUBMITTED",null,null);
    }

    public async Task<IReadOnlyList<WorkflowSubmission>> MySubmissions(Guid campus,Guid student,CancellationToken ct)
    {
        var actor=Actor();var results=new List<WorkflowSubmission>();
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(!await Owns(db,tx,actor,campus,student,"SELF",ct))throw new UnauthorizedAccessException();
        await using var cmd=new MySqlCommand("""
            SELECT sub.id,sub.assignment_id,sub.enrollment_id,sub.status,sub.score,sub.feedback
            FROM learning_submissions sub
            JOIN student_enrollments e ON e.tenant_id=sub.tenant_id AND e.campus_id=sub.campus_id
             AND e.id=sub.enrollment_id AND e.student_id=@student
            WHERE sub.tenant_id=@t AND sub.campus_id=@c
            ORDER BY sub.submitted_utc DESC LIMIT 100
            """,db,tx);
        Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(campus)),("@student",Id(student)));
        await using(var reader=await cmd.ExecuteReaderAsync(ct))
          while(await reader.ReadAsync(ct))
            results.Add(new WorkflowSubmission(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetString(3),reader.IsDBNull(4)?null:reader.GetDecimal(4),
                reader.IsDBNull(5)?null:reader.GetString(5)));
        await tx.CommitAsync(ct);return results;
    }

    public async Task<IReadOnlyList<WorkflowSubmission>> AssignedSubmissions(Guid campus,CancellationToken ct)
    {
        var actor=await authorizer.RequireAsync("learning.read",campus,ct);
        var results=new List<WorkflowSubmission>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT sub.id,sub.assignment_id,sub.enrollment_id,sub.status,sub.score,sub.feedback
            FROM learning_submissions sub
            JOIN learning_assignments a ON a.tenant_id=sub.tenant_id AND a.campus_id=sub.campus_id
              AND a.id=sub.assignment_id
            JOIN learning_lessons l ON l.tenant_id=a.tenant_id AND l.campus_id=a.campus_id
              AND l.id=a.lesson_id
            JOIN student_enrollments e ON e.tenant_id=sub.tenant_id AND e.campus_id=sub.campus_id
              AND e.id=sub.enrollment_id AND e.academic_year_id=l.academic_year_id
              AND e.academic_item_id=l.class_id
            JOIN academic_timetable_slots slot ON slot.tenant_id=l.tenant_id AND slot.campus_id=l.campus_id
              AND slot.class_item_id=l.class_id AND slot.academic_year_id=l.academic_year_id
              AND slot.subject_id=l.subject_id AND slot.is_active=1 AND slot.teacher_subject_id=@actor
            WHERE sub.tenant_id=@t AND sub.campus_id=@c
            ORDER BY sub.id LIMIT 200
            """,db);
        Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(campus)),("@actor",actor.SubjectId));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            results.Add(new WorkflowSubmission(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
                reader.GetString(3),reader.IsDBNull(4)?null:reader.GetDecimal(4),
                reader.IsDBNull(5)?null:reader.GetString(5)));
        return results;
    }

    public async Task<WorkflowSubmission?> Grade(TeacherGrade request,CancellationToken ct)
    {
        var actor=await authorizer.RequireAsync("learning.manage",request.CampusId,ct);
        if(request.SubmissionId==Guid.Empty||request.Score<0||request.Score>999999.99m||
           decimal.Round(request.Score,2)!=request.Score)throw new ArgumentException("Invalid grade.");
        var feedback=(request.Feedback??"").Trim();
        if(feedback.Length>1000)throw new ArgumentException("Feedback too long.");
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        Guid assignment,enrollment;
        decimal max;
        await using(var cmd=new MySqlCommand("""
            SELECT sub.assignment_id,sub.enrollment_id,a.max_score
            FROM learning_submissions sub
            JOIN learning_assignments a ON a.tenant_id=sub.tenant_id AND a.campus_id=sub.campus_id
             AND a.id=sub.assignment_id
            JOIN learning_lessons l ON l.tenant_id=a.tenant_id AND l.campus_id=a.campus_id
             AND l.id=a.lesson_id
            JOIN student_enrollments e ON e.tenant_id=sub.tenant_id AND e.campus_id=sub.campus_id
             AND e.id=sub.enrollment_id AND e.academic_year_id=l.academic_year_id
             AND e.academic_item_id=l.class_id
            JOIN tenant_memberships m ON m.tenant_id=sub.tenant_id AND m.subject_id=@actor AND m.is_active=1
            WHERE sub.id=@submission AND sub.tenant_id=@t AND sub.campus_id=@c
              AND sub.status='SUBMITTED'
              AND (m.role_code='ADMIN' OR EXISTS(
                SELECT 1 FROM academic_timetable_slots slot
                WHERE slot.tenant_id=l.tenant_id AND slot.campus_id=l.campus_id
                  AND slot.class_item_id=l.class_id AND slot.academic_year_id=l.academic_year_id
                  AND slot.subject_id=l.subject_id AND slot.teacher_subject_id=m.subject_id AND slot.is_active=1))
            FOR UPDATE
            """,db,tx))
        {
            Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
                ("@actor",actor.SubjectId),("@submission",Id(request.SubmissionId)));
            await using var reader=await cmd.ExecuteReaderAsync(ct);
            if(!await reader.ReadAsync(ct))return null;
            assignment=reader.GetGuid(0);enrollment=reader.GetGuid(1);max=reader.GetDecimal(2);
        }
        if(request.Score>max)throw new ArgumentException("Score exceeds maximum.");
        await Exec(db,tx,"""
            UPDATE learning_submissions SET status='GRADED',score=@score,feedback=@feedback,
               graded_by=@actor,graded_utc=UTC_TIMESTAMP(6)
            WHERE tenant_id=@t AND campus_id=@c AND id=@submission AND status='SUBMITTED'
            """,ct,("@score",request.Score),("@feedback",feedback),("@actor",actor.SubjectId),
             ("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
             ("@submission",Id(request.SubmissionId)));
        await tx.CommitAsync(ct);
        return new WorkflowSubmission(request.SubmissionId,assignment,enrollment,"GRADED",request.Score,feedback);
    }
}
