using EduVerse.Application.Learning;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlLearningStore(string connectionString):ILearningStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid id)=>id.ToString("D");
 private static void Add(MySqlCommand cmd,params (string Key,object? Value)[] parameters)
 {
  foreach(var (key,value) in parameters)cmd.Parameters.AddWithValue(key,value??DBNull.Value);
 }
 private static async Task<object?> Scalar(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,
   params (string Key,object? Value)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);return await cmd.ExecuteScalarAsync(ct);
 }
 private static async Task<int> Exec(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,
   params (string Key,object? Value)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);return await cmd.ExecuteNonQueryAsync(ct);
 }
 private static async Task<List<T>> Query<T>(MySqlConnection db,MySqlTransaction? tx,string sql,
  Func<MySqlDataReader,T> map,CancellationToken ct,params (string Key,object? Value)[] args)
 {
  var list=new List<T>();
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);
  await using var rd=await cmd.ExecuteReaderAsync(ct);
  while(await rd.ReadAsync(ct))list.Add(map(rd));
  return list;
 }
 private static async Task<bool> Exists(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,
  params (string Key,object? Value)[] args)=>
  Convert.ToInt32(await Scalar(db,tx,sql,ct,args))==1;
 private static Task<bool> Campus(MySqlConnection db,MySqlTransaction? tx,Guid tenant,Guid campus,CancellationToken ct)=>
  Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1)",
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 private static async Task LockTenant(MySqlConnection db,MySqlTransaction tx,Guid tenant,CancellationToken ct)
 {
  if(await Scalar(db,tx,"SELECT id FROM tenants WHERE id=@t AND is_active=1 FOR UPDATE",ct,
    ("@t",Id(tenant))) is null)throw new LearningScopeException();
 }
 private static DateTime Utc(DateTime date)=>DateTime.SpecifyKind(date,DateTimeKind.Utc);
 public async Task<bool> IsAdmin(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Exists(db,null,"""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
    JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
    JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
    JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
      AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
    JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='academics'
    JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
    WHERE m.tenant_id=@t AND m.subject_id=@user AND m.is_active=1 AND m.role_code='ADMIN')
   """,ct,("@t",Id(actor.TenantId)),("@user",actor.SubjectId));
 }
 private static LessonRecord MapLesson(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),
  r.GetGuid(2),r.GetGuid(3),r.GetString(4),r.GetString(5),
  r.IsDBNull(6)?null:r.GetString(6),r.GetString(7));
 private static AssignmentRecord MapAssignment(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),
  r.GetString(3),Utc(r.GetDateTime(4)),r.GetDecimal(5),r.GetString(6));
 private static SubmissionRecord MapSubmission(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),
  r.GetString(3),r.GetString(4),r.IsDBNull(5)?null:r.GetDecimal(5),r.IsDBNull(6)?null:r.GetString(6));
 public async Task<LessonRecord?> CreateLesson(Guid tenant,LessonCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct)||!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM academic_years y
    JOIN academic_catalog cl ON cl.tenant_id=y.tenant_id AND cl.campus_id=y.campus_id
     AND cl.id=@class AND cl.is_active=1 AND cl.kind IN ('GRADE','CLASS','SECTION','COURSE','BATCH')
    JOIN academic_catalog sub ON sub.tenant_id=y.tenant_id AND sub.campus_id=y.campus_id
     AND sub.id=@subject AND sub.kind='SUBJECT' AND sub.is_active=1
    WHERE y.tenant_id=@t AND y.campus_id=@c AND y.id=@year AND y.is_active=1)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@year",Id(r.AcademicYearId)),
   ("@class",Id(r.ClassId)),("@subject",Id(r.SubjectId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO learning_lessons(id,tenant_id,campus_id,academic_year_id,class_id,
    subject_id,title,body,resource_url)
   VALUES(@id,@t,@c,@year,@class,@subject,@title,@body,@url)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@year",Id(r.AcademicYearId)),
   ("@class",Id(r.ClassId)),("@subject",Id(r.SubjectId)),("@title",r.Title),
   ("@body",r.Body),("@url",r.ResourceUrl));
  await tx.CommitAsync(ct);
  return new LessonRecord(id,r.AcademicYearId,r.ClassId,r.SubjectId,r.Title,r.Body,r.ResourceUrl,"DRAFT");
 }
 public async Task<IReadOnlyList<LessonRecord>> Lessons(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,academic_year_id,class_id,subject_id,title,body,resource_url,status
   FROM learning_lessons WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 200
   """,MapLesson,ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<bool> PublishLesson(Guid tenant,Guid campus,Guid id,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Exec(db,null,"""
   UPDATE learning_lessons SET status='PUBLISHED',published_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='DRAFT'
   """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(id)))==1;
 }
 public async Task<AssignmentRecord?> CreateAssignment(Guid tenant,AssignmentCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM learning_lessons WHERE tenant_id=@t AND campus_id=@c
    AND id=@lesson AND status='PUBLISHED')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@lesson",Id(r.LessonId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO learning_assignments(id,tenant_id,campus_id,lesson_id,title,instructions,due_utc,max_score)
   VALUES(@id,@t,@c,@lesson,@title,@instructions,@due,@max)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@lesson",Id(r.LessonId)),
   ("@title",r.Title),("@instructions",r.Instructions),("@due",r.DueUtc),("@max",r.MaxScore));
  await tx.CommitAsync(ct);
  return new AssignmentRecord(id,r.LessonId,r.Title,r.Instructions,r.DueUtc,r.MaxScore,"DRAFT");
 }
 public async Task<IReadOnlyList<AssignmentRecord>> Assignments(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,lesson_id,title,instructions,due_utc,max_score,status
   FROM learning_assignments WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 300
   """,MapAssignment,ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<bool> PublishAssignment(Guid tenant,Guid campus,Guid id,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Exec(db,null,"""
   UPDATE learning_assignments SET status='PUBLISHED'
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='DRAFT'
   AND due_utc>UTC_TIMESTAMP(6)
   """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(id)))==1;
 }
 public async Task<SubmissionRecord?> Submit(Guid tenant,SubmissionCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM learning_assignments a
    JOIN learning_lessons l ON l.id=a.lesson_id AND l.tenant_id=a.tenant_id AND l.campus_id=a.campus_id
    JOIN student_enrollments e ON e.tenant_id=l.tenant_id AND e.campus_id=l.campus_id
      AND e.academic_year_id=l.academic_year_id AND e.academic_item_id=l.class_id
      AND e.id=@enrollment AND e.status='ACTIVE'
    JOIN students s ON s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id
      AND s.id=e.student_id AND s.status='ACTIVE'
    WHERE a.tenant_id=@t AND a.campus_id=@c AND a.id=@assignment
      AND a.status='PUBLISHED' AND l.status='PUBLISHED' AND a.due_utc>UTC_TIMESTAMP(6))
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@assignment",Id(r.AssignmentId)),("@enrollment",Id(r.EnrollmentId))))return null;
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM learning_submissions WHERE tenant_id=@t AND campus_id=@c
    AND assignment_id=@assignment AND enrollment_id=@enrollment)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@assignment",Id(r.AssignmentId)),
    ("@enrollment",Id(r.EnrollmentId))))
   throw new LearningConflictException("Submission already exists; grading will not overwrite it.");
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO learning_submissions(id,tenant_id,campus_id,assignment_id,enrollment_id,response_text)
   VALUES(@id,@t,@c,@assignment,@enrollment,@body)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@assignment",Id(r.AssignmentId)),("@enrollment",Id(r.EnrollmentId)),("@body",r.ResponseText));
  await tx.CommitAsync(ct);
  return new SubmissionRecord(id,r.AssignmentId,r.EnrollmentId,r.ResponseText,"SUBMITTED",null,null);
 }
 public async Task<IReadOnlyList<SubmissionRecord>> Submissions(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,assignment_id,enrollment_id,response_text,status,score,feedback
   FROM learning_submissions WHERE tenant_id=@t AND campus_id=@c ORDER BY submitted_utc DESC LIMIT 500
   """,MapSubmission,ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<SubmissionRecord?> Grade(Guid tenant,string actor,Guid campus,Guid submission,SubmissionGrade r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  var max=await Scalar(db,tx,"""
   SELECT a.max_score FROM learning_submissions s
   JOIN learning_assignments a ON a.id=s.assignment_id AND a.tenant_id=s.tenant_id AND a.campus_id=s.campus_id
   WHERE s.tenant_id=@t AND s.campus_id=@c AND s.id=@id AND s.status='SUBMITTED' FOR UPDATE
   """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(submission)));
  if(max is null)return null;
  if(r.Score>Convert.ToDecimal(max))throw new ArgumentException("Score exceeds assignment maximum.");
  await Exec(db,tx,"""
   UPDATE learning_submissions SET status='GRADED',score=@score,feedback=@feedback,
    graded_by=@actor,graded_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='SUBMITTED'
   """,ct,("@score",r.Score),("@feedback",r.Feedback),("@actor",actor),
    ("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(submission)));
  var result=await Query(db,tx,"""
   SELECT id,assignment_id,enrollment_id,response_text,status,score,feedback
   FROM learning_submissions WHERE tenant_id=@t AND campus_id=@c AND id=@id
   """,MapSubmission,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(submission)));
  await tx.CommitAsync(ct);return result.Single();
 }
 public async Task<StudentLearningProgress?> Progress(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  if(!await Exists(db,null,"SELECT EXISTS(SELECT 1 FROM student_enrollments WHERE tenant_id=@t AND campus_id=@c AND id=@enrollment)",
   ct,("@t",Id(tenant)),("@c",Id(campus)),("@enrollment",Id(enrollment))))return null;
  await using var cmd=new MySqlCommand("""
   SELECT COUNT(*),
    SUM(IF(s.id IS NOT NULL,1,0)),
    SUM(IF(s.status='GRADED',1,0)),
    COALESCE(AVG(IF(s.status='GRADED',100*s.score/a.max_score,NULL)),0)
   FROM learning_assignments a
   JOIN learning_lessons l ON l.id=a.lesson_id AND l.tenant_id=a.tenant_id AND l.campus_id=a.campus_id
   JOIN student_enrollments e ON e.tenant_id=l.tenant_id AND e.campus_id=l.campus_id
    AND e.academic_year_id=l.academic_year_id AND e.academic_item_id=l.class_id AND e.id=@enrollment
   LEFT JOIN learning_submissions s ON s.tenant_id=a.tenant_id AND s.campus_id=a.campus_id
     AND s.assignment_id=a.id AND s.enrollment_id=e.id
   WHERE a.tenant_id=@t AND a.campus_id=@c AND a.status='PUBLISHED'
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@enrollment",Id(enrollment)));
  await using var rd=await cmd.ExecuteReaderAsync(ct);await rd.ReadAsync(ct);
  return new StudentLearningProgress(enrollment,Convert.ToInt32(rd.GetInt64(0)),
   Convert.ToInt32(rd.IsDBNull(1)?0:rd.GetDecimal(1)),
   Convert.ToInt32(rd.IsDBNull(2)?0:rd.GetDecimal(2)),
   Math.Round(rd.GetDecimal(3),2,MidpointRounding.AwayFromZero));
 }
}
