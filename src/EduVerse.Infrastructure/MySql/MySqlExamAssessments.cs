using EduVerse.Application.Exams;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlExamStore
{
 public async Task<AssessmentRecord?> AddAssessmentAsync(Guid tenant,AssessmentCreate req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  // Serializes assessment creation with mark entry and approval transitions for an exam.
  Guid year;
  await using(var exam=new MySqlCommand("""
   SELECT academic_year_id FROM examinations WHERE id=@exam AND tenant_id=@t
    AND campus_id=@c AND status='DRAFT' FOR UPDATE
   """,db,tx))
  {
   Add(exam,("@exam",Id(req.ExamId)),("@t",Id(tenant)),("@c",Id(req.CampusId)));
   var value=await exam.ExecuteScalarAsync(ct);
   if(value is null)return null;
   year=value is Guid g?g:Guid.Parse(Convert.ToString(value)!);
  }
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM academic_catalog c
    JOIN academic_catalog s ON s.tenant_id=c.tenant_id AND s.campus_id=c.campus_id
      AND s.id=@subject AND s.kind='SUBJECT' AND s.is_active=1
    JOIN academic_years y ON y.tenant_id=c.tenant_id AND y.campus_id=c.campus_id AND y.id=@year
    WHERE c.tenant_id=@t AND c.campus_id=@campus AND c.id=@item
      AND c.kind IN ('GRADE','CLASS','SECTION','COURSE','BATCH')
      AND c.is_active=1 AND y.is_active=1 AND y.starts_on<=@date AND y.ends_on>=@date)
   """,ct,("@subject",Id(req.SubjectId)),("@year",Id(year)),
    ("@t",Id(tenant)),("@campus",Id(req.CampusId)),("@item",Id(req.AcademicItemId)),
    ("@date",Date(req.ScheduledOn))))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO exam_assessments(id,tenant_id,campus_id,exam_id,academic_item_id,subject_id,
    scheduled_on,starts_at,duration_minutes,max_marks,pass_marks,credits)
   VALUES(@id,@t,@c,@exam,@item,@subject,@date,@start,@duration,@max,@pass,@credits)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(req.CampusId)),("@exam",Id(req.ExamId)),
   ("@item",Id(req.AcademicItemId)),("@subject",Id(req.SubjectId)),("@date",Date(req.ScheduledOn)),
   ("@start",req.StartsAt?.ToTimeSpan()),("@duration",req.DurationMinutes),
   ("@max",req.MaxMarks),("@pass",req.PassMarks),("@credits",req.Credits));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new AssessmentRecord(id,req.ExamId,req.AcademicItemId,req.SubjectId,
   req.ScheduledOn,req.MaxMarks,req.PassMarks,req.Credits);
 }
 public async Task<IReadOnlyList<AssessmentRecord>> AssessmentsAsync(Guid tenant,Guid campus,Guid exam,CancellationToken ct)
 {
  var list=new List<AssessmentRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,exam_id,academic_item_id,subject_id,scheduled_on,max_marks,pass_marks,credits
   FROM exam_assessments WHERE tenant_id=@t AND campus_id=@c AND exam_id=@exam
   ORDER BY scheduled_on,subject_id LIMIT 200
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@exam",Id(exam)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new AssessmentRecord(r.GetGuid(0),r.GetGuid(1),
   r.GetGuid(2),r.GetGuid(3),DateOnly.FromDateTime(r.GetDateTime(4)),
   r.GetDecimal(5),r.GetDecimal(6),r.GetDecimal(7)));
  return list;
 }
 public async Task<MarkRecord?> EnterMarkAsync(Guid tenant,Guid actor,MarkCreate req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  Guid exam;
  decimal max;
  await using(var assessment=new MySqlCommand("""
   SELECT a.exam_id,a.max_marks FROM exam_assessments a
    JOIN examinations e ON e.id=a.exam_id AND e.tenant_id=a.tenant_id AND e.campus_id=a.campus_id
   WHERE a.id=@assessment AND a.tenant_id=@t AND a.campus_id=@c AND e.status='DRAFT'
   """,db,tx))
  {
   Add(assessment,("@assessment",Id(req.AssessmentId)),("@t",Id(tenant)),("@c",Id(req.CampusId)));
   await using var r=await assessment.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))return null;
   exam=r.GetGuid(0);max=r.GetDecimal(1);
  }
  await using(var lockExam=new MySqlCommand("""
   SELECT id FROM examinations WHERE id=@exam AND tenant_id=@t AND campus_id=@c AND status='DRAFT' FOR UPDATE
   """,db,tx))
  {
   Add(lockExam,("@exam",Id(exam)),("@t",Id(tenant)),("@c",Id(req.CampusId)));
   if(await lockExam.ExecuteScalarAsync(ct) is null)return null;
  }
  if(req.Marks>max)throw new ArgumentException("Marks exceed assessment maximum.");
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM student_enrollments en
    JOIN students s ON s.id=en.student_id AND s.tenant_id=en.tenant_id AND s.campus_id=en.campus_id
    JOIN exam_assessments a ON a.id=@assessment AND a.tenant_id=en.tenant_id
      AND a.campus_id=en.campus_id AND a.academic_item_id=en.academic_item_id
    JOIN examinations e ON e.id=a.exam_id AND e.academic_year_id=en.academic_year_id
      AND e.tenant_id=en.tenant_id AND e.campus_id=en.campus_id
    WHERE en.id=@enrollment AND en.tenant_id=@t AND en.campus_id=@c
      AND en.status='ACTIVE' AND s.status='ACTIVE')
   """,ct,("@assessment",Id(req.AssessmentId)),("@enrollment",Id(req.EnrollmentId)),
    ("@t",Id(tenant)),("@c",Id(req.CampusId))))return null;
  // Re-check teacher/class/subject ownership within the marks transaction.
  // A delegated teacher may only grade an assessment matching an active assigned timetable slot.
  await using(var guard=new MySqlCommand("""
   SELECT EXISTS (
    SELECT 1 FROM tenant_memberships m
    WHERE m.tenant_id=@t AND m.subject_id=@actor AND m.is_active=1
      AND (m.role_code='ADMIN' OR EXISTS (
        SELECT 1 FROM exam_assessments a
        JOIN examinations exam ON exam.tenant_id=a.tenant_id AND exam.campus_id=a.campus_id
            AND exam.id=a.exam_id
        JOIN student_enrollments en ON en.tenant_id=a.tenant_id AND en.campus_id=a.campus_id
            AND en.id=@enrollment AND en.academic_item_id=a.academic_item_id
            AND en.academic_year_id=exam.academic_year_id AND en.status='ACTIVE'
        JOIN academic_timetable_slots slot ON slot.tenant_id=a.tenant_id AND slot.campus_id=a.campus_id
            AND slot.class_item_id=en.academic_item_id AND slot.academic_year_id=en.academic_year_id
            AND slot.subject_id=a.subject_id AND slot.teacher_subject_id=m.subject_id AND slot.is_active=1
        WHERE a.id=@assessment AND a.tenant_id=@t AND a.campus_id=@c
      ))
   )
   """,db,tx))
  {
   Add(guard,("@t",Id(tenant)),("@c",Id(req.CampusId)),("@actor",Id(actor)),
       ("@assessment",Id(req.AssessmentId)),("@enrollment",Id(req.EnrollmentId)));
   if(Convert.ToInt32(await guard.ExecuteScalarAsync(ct))!=1)return null;
  }
  var id=Guid.NewGuid();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO exam_student_marks(id,tenant_id,campus_id,assessment_id,enrollment_id,marks,is_absent,entered_by)
   VALUES(@id,@t,@c,@assessment,@enrollment,@marks,@absent,@actor)
   ON DUPLICATE KEY UPDATE marks=VALUES(marks),is_absent=VALUES(is_absent),
       entered_by=VALUES(entered_by),entered_utc=UTC_TIMESTAMP(6),id=id
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(req.CampusId)),
    ("@assessment",Id(req.AssessmentId)),("@enrollment",Id(req.EnrollmentId)),
    ("@marks",req.Marks),("@absent",req.IsAbsent),("@actor",Id(actor)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await using(var lookup=new MySqlCommand("""
   SELECT id FROM exam_student_marks WHERE tenant_id=@t AND campus_id=@c
    AND assessment_id=@assessment AND enrollment_id=@enrollment
   """,db,tx))
  {
   Add(lookup,("@t",Id(tenant)),("@c",Id(req.CampusId)),
    ("@assessment",Id(req.AssessmentId)),("@enrollment",Id(req.EnrollmentId)));
   var obj=await lookup.ExecuteScalarAsync(ct);
   id=obj is Guid g?g:Guid.Parse(Convert.ToString(obj)!);
  }
  await tx.CommitAsync(ct);
  return new MarkRecord(id,req.AssessmentId,req.EnrollmentId,req.Marks,req.IsAbsent);
 }
 public async Task<IReadOnlyList<MarkRecord>> MarksAsync(Guid tenant,Guid campus,Guid assessment,CancellationToken ct)
 {
  var list=new List<MarkRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT m.id,m.assessment_id,m.enrollment_id,m.marks,m.is_absent
   FROM exam_student_marks m WHERE m.tenant_id=@t AND m.campus_id=@c AND m.assessment_id=@assessment
   ORDER BY m.enrollment_id LIMIT 500
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@assessment",Id(assessment)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new MarkRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),
   r.IsDBNull(3)?null:r.GetDecimal(3),r.GetBoolean(4)));
  return list;
 }
}
