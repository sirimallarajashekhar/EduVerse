using EduVerse.Application.Exams;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlExamStore
{
 private sealed record Score(Guid Enrollment,Guid Student,Guid Subject,string SubjectName,decimal? Marks,
  bool Absent,decimal Maximum,decimal Pass,decimal Credits,string Grade,decimal Points);
 private static async Task<List<Score>> Scores(MySqlConnection db,MySqlTransaction? tx,
     Guid tenant,Guid campus,Guid exam,CancellationToken ct)
 {
  var list=new List<Score>();
  await using var cmd=new MySqlCommand("""
   SELECT en.id,en.student_id,a.subject_id,sub.display_name,m.marks,m.is_absent,
      a.max_marks,a.pass_marks,a.credits,b.grade,b.grade_points
   FROM exam_assessments a
   JOIN examinations e ON e.id=a.exam_id AND e.tenant_id=a.tenant_id AND e.campus_id=a.campus_id
   JOIN student_enrollments en ON en.tenant_id=a.tenant_id AND en.campus_id=a.campus_id
     AND en.academic_year_id=e.academic_year_id AND en.academic_item_id=a.academic_item_id
     AND en.status='ACTIVE'
   JOIN students st ON st.id=en.student_id AND st.tenant_id=en.tenant_id
     AND st.campus_id=en.campus_id AND st.status='ACTIVE'
   JOIN academic_catalog sub ON sub.id=a.subject_id AND sub.tenant_id=a.tenant_id
     AND sub.campus_id=a.campus_id
   LEFT JOIN exam_student_marks m ON m.tenant_id=a.tenant_id AND m.campus_id=a.campus_id
     AND m.assessment_id=a.id AND m.enrollment_id=en.id
   LEFT JOIN exam_grade_bands b ON b.tenant_id=e.tenant_id AND b.campus_id=e.campus_id
     AND b.scheme_id=e.scheme_id AND
       (CASE WHEN m.is_absent=1 THEN 0 ELSE ROUND(100*m.marks/a.max_marks,2) END)
       BETWEEN b.min_percent AND b.max_percent
   WHERE a.tenant_id=@t AND a.campus_id=@c AND a.exam_id=@exam
   ORDER BY en.id,a.id
   """,db,tx);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@exam",Id(exam)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
  {
   if(r.IsDBNull(5) || r.IsDBNull(9))
     throw new ExamConflictException("Missing marks, grading band or incomplete assessment coverage.");
   list.Add(new Score(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),
     r.IsDBNull(4)?null:r.GetDecimal(4),r.GetBoolean(5),r.GetDecimal(6),
     r.GetDecimal(7),r.GetDecimal(8),r.GetString(9),r.GetDecimal(10)));
  }
  return list;
 }
 private static (decimal Total,decimal Max,decimal Percent,decimal Gpa,bool Passed) Aggregate(List<Score> scores)
 {
  var total=scores.Sum(s=>s.Marks??0m);
  var max=scores.Sum(s=>s.Maximum);
  var credits=scores.Sum(s=>s.Credits);
  if(max<=0||credits<=0)throw new ExamConflictException("Exam contains no assessable subjects.");
  var percent=Math.Round(total*100m/max,2,MidpointRounding.AwayFromZero);
  var gpa=Math.Round(scores.Sum(s=>s.Points*s.Credits)/credits,2,MidpointRounding.AwayFromZero);
  return(total,max,percent,gpa,scores.All(s=>!s.Absent && s.Marks>=s.Pass));
 }

 public async Task<bool> TransitionAsync(Guid tenant,Guid actor,Guid campus,Guid exam,string action,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  string? state;
  await using(var gate=new MySqlCommand("""
   SELECT status FROM examinations WHERE id=@exam AND tenant_id=@t AND campus_id=@c FOR UPDATE
   """,db,tx))
  {
   Add(gate,("@exam",Id(exam)),("@t",Id(tenant)),("@c",Id(campus)));
   state=await gate.ExecuteScalarAsync(ct) as string;
  }
  if(state is null)return false;
  if(action=="APPROVE"&&state!="DRAFT"||action=="PUBLISH"&&state!="APPROVED")
   throw new ExamConflictException("Exam is not in the expected workflow state.");
  var scores=await Scores(db,tx,tenant,campus,exam,ct);
  if(scores.Count==0)throw new ExamConflictException("No enrolled student marks exist for exam assessments.");
  // A draft cannot be approved until all active students in each assessed class have marks.
  var groups=scores.GroupBy(s=>s.Enrollment).ToArray();
  foreach(var group in groups)Aggregate(group.ToList());
  if(action=="PUBLISH")
  {
   foreach(var group in groups)
   {
    var totals=Aggregate(group.ToList());
    await using var insert=new MySqlCommand("""
     INSERT INTO exam_result_publications(id,tenant_id,campus_id,exam_id,enrollment_id,
       total_marks,maximum_marks,percentage,gpa,passed)
     VALUES(@id,@t,@c,@exam,@enrollment,@total,@max,@percent,@gpa,@passed)
     """,db,tx);
    var resultId=Guid.NewGuid();
    Add(insert,("@id",Id(resultId)),("@t",Id(tenant)),("@c",Id(campus)),
     ("@exam",Id(exam)),("@enrollment",Id(group.Key)),("@total",totals.Total),
     ("@max",totals.Max),("@percent",totals.Percent),("@gpa",totals.Gpa),
     ("@passed",totals.Passed));
    await insert.ExecuteNonQueryAsync(ct);
    foreach(var subject in group)
    {
     await using var detail=new MySqlCommand("""
      INSERT INTO exam_result_subject_snapshots
       (id,tenant_id,campus_id,result_id,subject_id,subject_name,grade,
        marks,max_marks,percentage,grade_points,passed,absent)
      VALUES(@id,@t,@c,@result,@subject,@name,@grade,@marks,@max,
       @percentage,@points,@passed,@absent)
      """,db,tx);
     Add(detail,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(campus)),
      ("@result",Id(resultId)),("@subject",Id(subject.Subject)),
      ("@name",subject.SubjectName),("@grade",subject.Grade),("@marks",subject.Marks),
      ("@max",subject.Maximum),
      ("@percentage",Math.Round((subject.Marks??0m)*100m/subject.Maximum,2,MidpointRounding.AwayFromZero)),
      ("@points",subject.Points),("@passed",!subject.Absent&&subject.Marks>=subject.Pass),
      ("@absent",subject.Absent));
     await detail.ExecuteNonQueryAsync(ct);
    }
   }
  }
  await using(var update=new MySqlCommand("""
   UPDATE examinations SET status=@state,
     approved_by=IF(@state='APPROVED',@actor,approved_by),
     approved_utc=IF(@state='APPROVED',UTC_TIMESTAMP(6),approved_utc),
     published_utc=IF(@state='PUBLISHED',UTC_TIMESTAMP(6),published_utc)
   WHERE tenant_id=@t AND campus_id=@c AND id=@exam
   """,db,tx))
  {
   Add(update,("@state",action=="APPROVE"?"APPROVED":"PUBLISHED"),
    ("@actor",Id(actor)),("@t",Id(tenant)),("@c",Id(campus)),("@exam",Id(exam)));
   await update.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return true;
 }

 public async Task<ResultRecord?> ResultAsync(Guid tenant,Guid campus,Guid exam,Guid enrollment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using(var check=new MySqlCommand("""
   SELECT r.total_marks,r.maximum_marks,r.percentage,r.gpa,r.passed,
     CONCAT(s.given_name,' ',s.family_name),s.admission_number,e.title
   FROM exam_result_publications r
   JOIN examinations e ON e.id=r.exam_id AND e.tenant_id=r.tenant_id AND e.campus_id=r.campus_id
   JOIN student_enrollments en ON en.id=r.enrollment_id AND en.tenant_id=r.tenant_id AND en.campus_id=r.campus_id
   JOIN students s ON s.id=en.student_id AND s.tenant_id=en.tenant_id AND s.campus_id=en.campus_id
   WHERE r.tenant_id=@t AND r.campus_id=@c AND r.exam_id=@exam AND r.enrollment_id=@enrollment
    AND e.status='PUBLISHED'
   """,db))
  {
   Add(check,("@t",Id(tenant)),("@c",Id(campus)),
    ("@exam",Id(exam)),("@enrollment",Id(enrollment)));
   await using var r=await check.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))return null;
   var total=r.GetDecimal(0);var max=r.GetDecimal(1);var percentage=r.GetDecimal(2);
   var gpa=r.GetDecimal(3);var passed=r.GetBoolean(4);
   var student=r.GetString(5);var number=r.GetString(6);var title=r.GetString(7);
   await r.DisposeAsync();
   var lines=new List<ResultLine>();
   await using(var details=new MySqlCommand("""
    SELECT s.subject_name,s.grade,s.marks,s.max_marks,s.percentage,
     s.grade_points,s.passed,s.absent
    FROM exam_result_subject_snapshots s
    JOIN exam_result_publications r ON r.id=s.result_id AND r.tenant_id=s.tenant_id
      AND r.campus_id=s.campus_id
    WHERE s.tenant_id=@t AND s.campus_id=@c AND r.exam_id=@exam
      AND r.enrollment_id=@enrollment
    ORDER BY s.subject_name
    """,db))
   {
    Add(details,("@t",Id(tenant)),("@c",Id(campus)),
     ("@exam",Id(exam)),("@enrollment",Id(enrollment)));
    await using var reader=await details.ExecuteReaderAsync(ct);
    while(await reader.ReadAsync(ct))
     lines.Add(new ResultLine(reader.GetString(0),reader.GetString(1),
      reader.IsDBNull(2)?null:reader.GetDecimal(2),reader.GetDecimal(3),
      reader.GetDecimal(4),reader.GetDecimal(5),reader.GetBoolean(6),reader.GetBoolean(7)));
   }
   return new ResultRecord(exam,enrollment,student,number,title,total,max,percentage,gpa,passed,lines);
  }
 }

 public async Task<ExamAnalytics?> AnalyticsAsync(Guid tenant,Guid campus,Guid exam,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT COUNT(*),COALESCE(AVG(percentage),0),
     COALESCE(AVG(IF(passed=1,100,0)),0),
     COALESCE(MAX(percentage),0),COALESCE(MIN(percentage),0)
   FROM exam_result_publications r
   JOIN examinations e ON e.id=r.exam_id AND e.tenant_id=r.tenant_id AND e.campus_id=r.campus_id
   WHERE r.tenant_id=@t AND r.campus_id=@c AND r.exam_id=@exam AND e.status='PUBLISHED'
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@exam",Id(exam)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  await r.ReadAsync(ct);
  if(r.GetInt64(0)==0)return null;
  return new ExamAnalytics(Convert.ToInt32(r.GetInt64(0)),Math.Round(r.GetDecimal(1),2),
   Math.Round(r.GetDecimal(2),2),r.GetDecimal(3),r.GetDecimal(4));
 }
 public async Task<CgpaRecord?> CgpaAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT COUNT(*),COALESCE(AVG(r.gpa),0)
   FROM exam_result_publications r
   JOIN student_enrollments en ON en.id=r.enrollment_id AND en.tenant_id=r.tenant_id AND en.campus_id=r.campus_id
   JOIN examinations e ON e.id=r.exam_id AND e.tenant_id=r.tenant_id AND e.campus_id=r.campus_id
   WHERE r.tenant_id=@t AND r.campus_id=@c AND en.student_id=@student AND e.status='PUBLISHED'
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@student",Id(student)));
  await using var r=await cmd.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
  if(r.GetInt64(0)==0)return null;
  return new CgpaRecord(student,Math.Round(r.GetDecimal(1),2),Convert.ToInt32(r.GetInt64(0)));
 }
}
