using EduVerse.Application.Learning;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlLearningStore
{
 public async Task<AnnouncementRecord?> Announce(Guid tenant,AnnouncementCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO learning_announcements(id,tenant_id,campus_id,title,body,audience)
   VALUES(@id,@t,@c,@title,@body,@audience)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@title",r.Title),("@body",r.Body),("@audience",r.Audience));
  // Delivery here means stored in the in-app inbox; no claim of external sending.
  // GUARDIANS is a staff-facing announcement until guardians have authenticated accounts.
  await Exec(db,tx,"""
   INSERT INTO learning_notifications(id,tenant_id,campus_id,recipient_subject,source_id,title,body)
   SELECT UUID(),@t,@c,m.subject_id,@source,@title,LEFT(@body,1000)
   FROM tenant_memberships m JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
   WHERE m.tenant_id=@t AND m.is_active=1
    AND (@audience='ALL' OR @audience='STAFF' AND m.role_code IN ('ADMIN','TEACHER','STAFF')
         OR @audience='GUARDIANS' AND m.role_code='ADMIN')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@source",Id(id)),
   ("@title",r.Title),("@body",r.Body),("@audience",r.Audience));
  await tx.CommitAsync(ct);
  return new AnnouncementRecord(id,r.Title,r.Body,r.Audience,DateTime.UtcNow);
 }
 public async Task<IReadOnlyList<AnnouncementRecord>> Announcements(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,title,body,audience,published_utc FROM learning_announcements
   WHERE tenant_id=@t AND campus_id=@c ORDER BY published_utc DESC LIMIT 250
   """,r=>new AnnouncementRecord(r.GetGuid(0),r.GetString(1),r.GetString(2),
     r.GetString(3),Utc(r.GetDateTime(4))),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<ParentThreadRecord?> OpenThread(Guid tenant,ParentThreadCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM student_guardians g
    JOIN students s ON s.id=g.student_id AND s.tenant_id=g.tenant_id AND s.campus_id=g.campus_id
    WHERE g.id=@guardian AND g.student_id=@student AND g.tenant_id=@t
      AND g.campus_id=@c AND s.status='ACTIVE')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@guardian",Id(r.GuardianId)),("@student",Id(r.StudentId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO learning_parent_threads(id,tenant_id,campus_id,student_id,guardian_id,subject)
   VALUES(@id,@t,@c,@student,@guardian,@subject)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@student",Id(r.StudentId)),("@guardian",Id(r.GuardianId)),("@subject",r.Subject));
  await tx.CommitAsync(ct);
  return new ParentThreadRecord(id,r.StudentId,r.GuardianId,r.Subject,"OPEN");
 }
 public async Task<IReadOnlyList<ParentThreadRecord>> Threads(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,student_id,guardian_id,subject,status FROM learning_parent_threads
   WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 250
   """,r=>new ParentThreadRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),
    r.GetString(3),r.GetString(4)),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<ParentMessageRecord?> SendMessage(Guid tenant,string actor,ParentMessageCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM learning_parent_threads
    WHERE tenant_id=@t AND campus_id=@c AND id=@thread AND status='OPEN')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@thread",Id(r.ThreadId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO learning_parent_messages(id,tenant_id,campus_id,thread_id,author_subject,body)
   VALUES(@id,@t,@c,@thread,@actor,@body)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@thread",Id(r.ThreadId)),("@actor",actor),("@body",r.Body));
  await tx.CommitAsync(ct);
  return new ParentMessageRecord(id,r.ThreadId,actor,r.Body,DateTime.UtcNow);
 }
 public async Task<IReadOnlyList<ParentMessageRecord>> Messages(Guid tenant,Guid campus,Guid thread,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,thread_id,author_subject,body,created_utc FROM learning_parent_messages
   WHERE tenant_id=@t AND campus_id=@c AND thread_id=@thread ORDER BY created_utc LIMIT 500
   """,r=>new ParentMessageRecord(r.GetGuid(0),r.GetGuid(1),r.GetString(2),
     r.GetString(3),Utc(r.GetDateTime(4))),
   ct,("@t",Id(tenant)),("@c",Id(campus)),("@thread",Id(thread)));
 }
 public async Task<IReadOnlyList<NotificationRecord>> Notifications(Guid tenant,Guid campus,string subject,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,title,body,status,delivered_utc,read_utc FROM learning_notifications
   WHERE tenant_id=@t AND campus_id=@c AND recipient_subject=@subject
   ORDER BY delivered_utc DESC LIMIT 250
   """,r=>new NotificationRecord(r.GetGuid(0),r.GetString(1),r.GetString(2),
    r.GetString(3),Utc(r.GetDateTime(4)),r.IsDBNull(5)?null:Utc(r.GetDateTime(5))),
   ct,("@t",Id(tenant)),("@c",Id(campus)),("@subject",subject));
 }
 public async Task<bool> MarkRead(Guid tenant,Guid campus,string subject,Guid id,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Exec(db,null,"""
   UPDATE learning_notifications SET status='READ',read_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND recipient_subject=@subject
     AND status='DELIVERED'
   """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@subject",subject),("@id",Id(id)))==1;
 }
 public async Task<LearningDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
    (SELECT COUNT(*) FROM learning_lessons WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM learning_assignments WHERE tenant_id=@t AND campus_id=@c AND status='PUBLISHED'),
    (SELECT COUNT(*) FROM learning_submissions WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM learning_submissions WHERE tenant_id=@t AND campus_id=@c AND status='GRADED'),
    (SELECT COALESCE(AVG(100*s.score/a.max_score),0) FROM learning_submissions s
       JOIN learning_assignments a ON a.id=s.assignment_id AND a.tenant_id=s.tenant_id AND a.campus_id=s.campus_id
       WHERE s.tenant_id=@t AND s.campus_id=@c AND s.status='GRADED'),
    (SELECT COUNT(*) FROM learning_announcements WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM learning_parent_threads WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM learning_notifications WHERE tenant_id=@t AND campus_id=@c AND status='DELIVERED')
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var rd=await cmd.ExecuteReaderAsync(ct);await rd.ReadAsync(ct);
  return new LearningDashboard(Convert.ToInt32(rd.GetInt64(0)),Convert.ToInt32(rd.GetInt64(1)),
   Convert.ToInt32(rd.GetInt64(2)),Convert.ToInt32(rd.GetInt64(3)),
   Math.Round(rd.GetDecimal(4),2,MidpointRounding.AwayFromZero),
   Convert.ToInt32(rd.GetInt64(5)),Convert.ToInt32(rd.GetInt64(6)),Convert.ToInt32(rd.GetInt64(7)));
 }
}
