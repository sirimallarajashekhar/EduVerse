using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Api.Security;

public sealed partial class RoleLearningWorkflowStore
{
    public async Task<WorkflowThread?> OpenConversation(ParentConversation request,CancellationToken ct)
    {
        var actor=Actor();
        if(request.CampusId==Guid.Empty||request.StudentId==Guid.Empty)
            throw new ArgumentException("Invalid student or campus.");
        var subject=Required(request.Subject,160);
        var body=Required(request.Body,5000);
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(!await Owns(db,tx,actor,request.CampusId,request.StudentId,"PARENT",ct))return null;
        Guid guardian;
        await using(var lookup=new MySqlCommand("""
            SELECT g.id FROM student_guardians g
            JOIN identity_users u ON u.id=@actor AND u.is_active=1
            WHERE g.tenant_id=@t AND g.campus_id=@c AND g.student_id=@student
              AND g.contact_email IS NOT NULL
              AND UPPER(TRIM(g.contact_email))=u.email_normalized
            ORDER BY g.is_primary DESC LIMIT 1
            """,db,tx))
        {
            Add(lookup,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
                ("@student",Id(request.StudentId)),("@actor",actor.SubjectId));
            var value=await lookup.ExecuteScalarAsync(ct);
            if(value is null)return null;
            guardian=value is Guid guid?guid:Guid.Parse(Convert.ToString(value)!);
        }
        var thread=Guid.NewGuid();var message=Guid.NewGuid();
        await Exec(db,tx,"""
            INSERT INTO learning_parent_threads(id,tenant_id,campus_id,student_id,guardian_id,subject)
            VALUES(@id,@t,@c,@student,@guardian,@subject)
            """,ct,("@id",Id(thread)),("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
             ("@student",Id(request.StudentId)),("@guardian",Id(guardian)),("@subject",subject));
        await Exec(db,tx,"""
            INSERT INTO learning_parent_messages(id,tenant_id,campus_id,thread_id,author_subject,body)
            VALUES(@id,@t,@c,@thread,@actor,@body)
            """,ct,("@id",Id(message)),("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
             ("@thread",Id(thread)),("@actor",actor.SubjectId),("@body",body));
        // In-app notifications only, to active teachers assigned to this student's class.
        await Exec(db,tx,"""
            INSERT INTO learning_notifications(id,tenant_id,campus_id,recipient_subject,source_id,title,body)
            SELECT UUID(),@t,@c,staff.teacher_subject_id,@thread,'Parent message',LEFT(@body,1000)
            FROM student_enrollments e
            JOIN academic_timetable_slots staff ON staff.tenant_id=e.tenant_id AND staff.campus_id=e.campus_id
             AND staff.class_item_id=e.academic_item_id AND staff.academic_year_id=e.academic_year_id
             AND staff.is_active=1
            JOIN tenant_memberships member ON member.tenant_id=staff.tenant_id
             AND member.subject_id=staff.teacher_subject_id AND member.is_active=1
            WHERE e.tenant_id=@t AND e.campus_id=@c AND e.student_id=@student AND e.status='ACTIVE'
            GROUP BY staff.teacher_subject_id
            """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
             ("@student",Id(request.StudentId)),("@thread",Id(thread)),("@body",body));
        await tx.CommitAsync(ct);
        return new WorkflowThread(thread,request.StudentId,subject,"OPEN");
    }

    private async Task<bool> CanReadThread(MySqlConnection db,MySqlTransaction tx,TenantIdentity actor,
        Guid campus,Guid thread,CancellationToken ct)=>
        await Exists(db,tx,"""
            SELECT EXISTS(SELECT 1 FROM learning_parent_threads thread
            JOIN student_guardians g ON g.tenant_id=thread.tenant_id AND g.campus_id=thread.campus_id
             AND g.id=thread.guardian_id AND g.student_id=thread.student_id
            JOIN tenant_memberships m ON m.tenant_id=thread.tenant_id AND m.subject_id=@actor AND m.is_active=1
            JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
            WHERE thread.tenant_id=@t AND thread.campus_id=@c AND thread.id=@thread
             AND (m.role_code='ADMIN' OR
                  (EXISTS(SELECT 1 FROM student_portal_links link
                   WHERE link.tenant_id=thread.tenant_id AND link.campus_id=thread.campus_id
                    AND link.student_id=thread.student_id AND link.subject_id=m.subject_id
                    AND link.relationship_code='PARENT')
                   AND g.contact_email IS NOT NULL AND UPPER(TRIM(g.contact_email))=u.email_normalized)
                  OR EXISTS(SELECT 1 FROM student_enrollments e
                   JOIN academic_timetable_slots slot ON slot.tenant_id=e.tenant_id AND slot.campus_id=e.campus_id
                    AND slot.class_item_id=e.academic_item_id AND slot.academic_year_id=e.academic_year_id
                    AND slot.teacher_subject_id=m.subject_id AND slot.is_active=1
                   WHERE e.tenant_id=thread.tenant_id AND e.campus_id=thread.campus_id
                    AND e.student_id=thread.student_id AND e.status='ACTIVE')))
            """,ct,("@t",Id(actor.TenantId)),("@c",Id(campus)),
                ("@thread",Id(thread)),("@actor",actor.SubjectId));

    public async Task<IReadOnlyList<WorkflowThread>> Conversations(Guid campus,CancellationToken ct)
    {
        var actor=Actor();
        if(campus==Guid.Empty)throw new ArgumentException("Campus required.");
        var result=new List<WorkflowThread>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT DISTINCT thread.id,thread.student_id,thread.subject,thread.status
            FROM learning_parent_threads thread
            JOIN student_guardians g ON g.tenant_id=thread.tenant_id AND g.campus_id=thread.campus_id
             AND g.id=thread.guardian_id AND g.student_id=thread.student_id
            JOIN tenant_memberships m ON m.tenant_id=thread.tenant_id AND m.subject_id=@actor AND m.is_active=1
            JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
            WHERE thread.tenant_id=@t AND thread.campus_id=@c
              AND (m.role_code='ADMIN' OR
                 (EXISTS(SELECT 1 FROM student_portal_links link
                   WHERE link.tenant_id=thread.tenant_id AND link.campus_id=thread.campus_id
                    AND link.student_id=thread.student_id AND link.subject_id=m.subject_id
                    AND link.relationship_code='PARENT')
                   AND g.contact_email IS NOT NULL AND UPPER(TRIM(g.contact_email))=u.email_normalized)
                 OR EXISTS(SELECT 1 FROM student_enrollments e
                   JOIN academic_timetable_slots slot ON slot.tenant_id=e.tenant_id AND slot.campus_id=e.campus_id
                    AND slot.class_item_id=e.academic_item_id AND slot.academic_year_id=e.academic_year_id
                    AND slot.teacher_subject_id=m.subject_id AND slot.is_active=1
                   WHERE e.tenant_id=thread.tenant_id AND e.campus_id=thread.campus_id
                     AND e.student_id=thread.student_id AND e.status='ACTIVE'))
            ORDER BY thread.id LIMIT 100
            """,db);
        Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(campus)),("@actor",actor.SubjectId));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            result.Add(new WorkflowThread(reader.GetGuid(0),reader.GetGuid(1),
                reader.GetString(2),reader.GetString(3)));
        return result;
    }

    public async Task<IReadOnlyList<WorkflowMessage>?> Messages(Guid campus,Guid thread,CancellationToken ct)
    {
        var actor=Actor();
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(!await CanReadThread(db,tx,actor,campus,thread,ct))return null;
        var results=new List<WorkflowMessage>();
        await using var cmd=new MySqlCommand("""
            SELECT id,author_subject,body,created_utc
            FROM learning_parent_messages
            WHERE tenant_id=@t AND campus_id=@c AND thread_id=@thread
            ORDER BY created_utc LIMIT 300
            """,db,tx);
        Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(campus)),("@thread",Id(thread)));
        await using(var rd=await cmd.ExecuteReaderAsync(ct))
            while(await rd.ReadAsync(ct))
                results.Add(new WorkflowMessage(rd.GetGuid(0),rd.GetString(1),
                    rd.GetString(2),DateTime.SpecifyKind(rd.GetDateTime(3),DateTimeKind.Utc)));
        await tx.CommitAsync(ct);return results;
    }

    public async Task<WorkflowMessage?> Reply(ConversationReply request,CancellationToken ct)
    {
        var actor=Actor();
        if(request.CampusId==Guid.Empty||request.ThreadId==Guid.Empty)
            throw new ArgumentException("Missing campus/thread.");
        var body=Required(request.Body,5000);
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        if(!await CanReadThread(db,tx,actor,request.CampusId,request.ThreadId,ct)||
           !await Exists(db,tx,"""
            SELECT EXISTS(SELECT 1 FROM learning_parent_threads
              WHERE tenant_id=@t AND campus_id=@c AND id=@thread AND status='OPEN')
            """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),("@thread",Id(request.ThreadId))))
            return null;
        var id=Guid.NewGuid();
        await Exec(db,tx,"""
          INSERT INTO learning_parent_messages(id,tenant_id,campus_id,thread_id,author_subject,body)
          VALUES(@id,@t,@c,@thread,@actor,@body)
          """,ct,("@id",Id(id)),("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
              ("@thread",Id(request.ThreadId)),("@actor",actor.SubjectId),("@body",body));
        await Exec(db,tx,"""
          INSERT INTO learning_notifications(id,tenant_id,campus_id,recipient_subject,source_id,title,body)
          SELECT UUID(),@t,@c,link.subject_id,@thread,'Conversation reply',LEFT(@body,1000)
          FROM learning_parent_threads thread
          JOIN student_portal_links link ON link.tenant_id=thread.tenant_id
            AND link.campus_id=thread.campus_id AND link.student_id=thread.student_id
            AND link.relationship_code='PARENT'
          JOIN tenant_memberships member ON member.tenant_id=link.tenant_id
            AND member.subject_id=link.subject_id AND member.is_active=1
          WHERE thread.tenant_id=@t AND thread.campus_id=@c AND thread.id=@thread
           AND link.subject_id<>@actor
          """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),
           ("@thread",Id(request.ThreadId)),("@actor",actor.SubjectId),("@body",body));
        await tx.CommitAsync(ct);
        return new WorkflowMessage(id,actor.SubjectId,body,DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<LearningReview>> Reviews(Guid campus,CancellationToken ct)
    {
        var actor=await Admin(ct);var results=new List<LearningReview>();
        await using var db=Connect();await db.OpenAsync(ct);
        await using var cmd=new MySqlCommand("""
            SELECT id,'LESSON',title,status FROM learning_lessons
             WHERE tenant_id=@t AND campus_id=@c AND status='DRAFT'
            UNION ALL
            SELECT id,'ASSIGNMENT',title,status FROM learning_assignments
             WHERE tenant_id=@t AND campus_id=@c AND status='DRAFT'
            LIMIT 200
            """,db);
        Add(cmd,("@t",Id(actor.TenantId)),("@c",Id(campus)));
        await using var reader=await cmd.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct))
            results.Add(new LearningReview(reader.GetGuid(0),reader.GetString(1),
                reader.GetString(2),reader.GetString(3)));
        return results;
    }

    public async Task<bool> Publish(string kind,LearningApproval request,CancellationToken ct)
    {
        var actor=await Admin(ct);
        if(request.CampusId==Guid.Empty||request.ContentId==Guid.Empty||
           kind is not ("LESSON" or "ASSIGNMENT"))
            throw new ArgumentException("Invalid approval.");
        await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        var changed=kind=="LESSON"
            ?await Exec(db,tx,"""
                UPDATE learning_lessons SET status='PUBLISHED',published_utc=UTC_TIMESTAMP(6)
                WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='DRAFT'
                """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),("@id",Id(request.ContentId)))
            :await Exec(db,tx,"""
                UPDATE learning_assignments a
                JOIN learning_lessons l ON l.id=a.lesson_id AND l.tenant_id=a.tenant_id
                  AND l.campus_id=a.campus_id AND l.status='PUBLISHED'
                SET a.status='PUBLISHED'
                WHERE a.tenant_id=@t AND a.campus_id=@c AND a.id=@id
                  AND a.status='DRAFT' AND a.due_utc>UTC_TIMESTAMP(6)
                """,ct,("@t",Id(actor.TenantId)),("@c",Id(request.CampusId)),("@id",Id(request.ContentId)));
        if(changed!=1)return false;
        await Exec(db,tx,"""
            INSERT INTO identity_audit_events(tenant_id,user_id,event_code)
            VALUES(@t,@actor,@code)
            """,ct,("@t",Id(actor.TenantId)),("@actor",actor.SubjectId),
             ("@code",kind=="LESSON"?"LEARNING_LESSON_PUBLISHED":"LEARNING_ASSIGNMENT_PUBLISHED"));
        await tx.CommitAsync(ct);return true;
    }
}
