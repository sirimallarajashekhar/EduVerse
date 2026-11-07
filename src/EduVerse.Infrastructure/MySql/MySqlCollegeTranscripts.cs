using EduVerse.Application.College;
using EduVerse.Contracts;
using MySqlConnector;
namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlCollegeStore
{
 private static CollegeSemesterResult MapResult(MySqlDataReader x)=>new(x.GetGuid(0),x.GetGuid(1),
  x.GetDecimal(2),x.GetDecimal(3),x.GetDecimal(4),x.GetDecimal(5),x.GetInt32(6),x.GetBoolean(7));
 public async Task<CollegeSemesterResult?> Finalize(Guid tenant,string actor,CollegeFinalize r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  var rule=await Query(db,tx,"""
   SELECT p.id,rule.passing_points,rule.min_credits_for_progression,rule.allow_backlogs,e.student_id
   FROM college_semesters sem
   JOIN college_programs p ON p.id=sem.program_id AND p.tenant_id=sem.tenant_id AND p.campus_id=sem.campus_id
   JOIN college_regulations rule ON rule.id=p.regulation_id AND rule.tenant_id=p.tenant_id AND rule.campus_id=p.campus_id
   JOIN student_enrollments e ON e.id=@enroll AND e.tenant_id=sem.tenant_id AND e.campus_id=sem.campus_id
    AND e.academic_year_id=sem.academic_year_id
   JOIN academic_catalog item ON item.id=e.academic_item_id AND item.tenant_id=e.tenant_id
     AND item.campus_id=e.campus_id AND item.kind='COURSE' AND item.parent_id=p.catalog_program_id
   WHERE sem.id=@semester AND sem.tenant_id=@t AND sem.campus_id=@c AND sem.status='OPEN'
   """,x=>(Program:x.GetGuid(0),Passing:x.GetDecimal(1),Min:x.GetDecimal(2),
    AllowBacklogs:x.GetBoolean(3),Student:x.GetGuid(4)),ct,
   ("@semester",Id(r.SemesterId)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@enroll",Id(r.EnrollmentId)));
  if(rule.Count==0)return null;
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM college_semester_results WHERE tenant_id=@t AND campus_id=@c
    AND enrollment_id=@enroll AND semester_id=@semester)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@enroll",Id(r.EnrollmentId)),
   ("@semester",Id(r.SemesterId))))
   throw new CollegeConflictException("Semester result is already finalized.");
  var courses=await Query(db,tx,"""
   SELECT course.id,course.course_type,course.credits,reg.status,reg.grade_points,
      reg.passed
   FROM college_courses course
   LEFT JOIN college_registrations reg ON reg.course_id=course.id
     AND reg.tenant_id=course.tenant_id AND reg.campus_id=course.campus_id
     AND reg.enrollment_id=@enroll
   WHERE course.tenant_id=@t AND course.campus_id=@c AND course.semester_id=@semester
   """,x=>(Id:x.GetGuid(0),Type:x.GetString(1),Credits:x.GetDecimal(2),
    Status:x.IsDBNull(3)?null:x.GetString(3),Points:x.IsDBNull(4)?(decimal?)null:x.GetDecimal(4),
    Passed:x.IsDBNull(5)?(bool?)null:x.GetBoolean(5)),ct,
   ("@t",Id(tenant)),("@c",Id(r.CampusId)),("@semester",Id(r.SemesterId)),("@enroll",Id(r.EnrollmentId)));
  if(courses.Count==0||courses.Any(c=>c.Type=="CORE"&&c.Status is null)||
    courses.Any(c=>c.Status=="ENROLLED")||courses.All(c=>c.Status is null))
   throw new CollegeConflictException("All mandatory core courses must be registered and all registered grades posted.");
  var graded=courses.Where(c=>c.Status is "PASSED" or "BACKLOG").ToArray();
  if(graded.Length==0)throw new CollegeConflictException("No graded courses for this semester.");
  var credits=graded.Sum(x=>x.Credits);
  var earned=graded.Where(x=>x.Passed==true).Sum(x=>x.Credits);
  var weighted=graded.Sum(x=>x.Credits*x.Points!.Value);
  var sgpa=Math.Round(weighted/credits,2,MidpointRounding.AwayFromZero);
  var backlog=graded.Count(x=>x.Passed==false);
  var policy=rule[0];
  var eligible=earned>=policy.Min && (policy.AllowBacklogs||backlog==0);
  await Exec(db,tx,"""
   INSERT INTO college_semester_results
    (id,tenant_id,campus_id,enrollment_id,semester_id,attempted_credits,earned_credits,
     weighted_points,sgpa,backlogs,progression_eligible,finalized_by)
   VALUES(@id,@t,@c,@enroll,@semester,@attempted,@earned,@weighted,@sgpa,@backlogs,@eligible,@actor)
   """,ct,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@enroll",Id(r.EnrollmentId)),("@semester",Id(r.SemesterId)),("@attempted",credits),
    ("@earned",earned),("@weighted",weighted),("@sgpa",sgpa),("@backlogs",backlog),
    ("@eligible",eligible),("@actor",actor));
  await tx.CommitAsync(ct);
  return new CollegeSemesterResult(r.EnrollmentId,r.SemesterId,credits,earned,weighted,sgpa,backlog,eligible);
 }
 public async Task<CollegeTranscript?> Transcript(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  var scope=await Query(db,null,"""
   SELECT e.student_id,p.id FROM student_enrollments e
   JOIN academic_catalog item ON item.tenant_id=e.tenant_id AND item.campus_id=e.campus_id
     AND item.id=e.academic_item_id AND item.kind='COURSE'
   JOIN college_programs p ON p.tenant_id=e.tenant_id AND p.campus_id=e.campus_id
     AND p.catalog_program_id=item.parent_id
   WHERE e.tenant_id=@t AND e.campus_id=@c AND e.id=@enroll
   """,x=>(Student:x.GetGuid(0),Program:x.GetGuid(1)),ct,
   ("@t",Id(tenant)),("@c",Id(campus)),("@enroll",Id(enrollment)));
  if(scope.Count==0)return null;
  var records=await Query(db,null,"""
   SELECT result.enrollment_id,result.semester_id,result.attempted_credits,result.earned_credits,
      result.weighted_points,result.sgpa,result.backlogs,result.progression_eligible
   FROM college_semester_results result
   JOIN college_semesters sem ON sem.id=result.semester_id
     AND sem.tenant_id=result.tenant_id AND sem.campus_id=result.campus_id
     AND sem.program_id=@program
   JOIN student_enrollments e ON e.id=result.enrollment_id AND e.tenant_id=result.tenant_id
     AND e.campus_id=result.campus_id AND e.student_id=@student
   WHERE result.tenant_id=@t AND result.campus_id=@c
   ORDER BY sem.semester_number
   """,MapResult,ct,("@t",Id(tenant)),("@c",Id(campus)),
    ("@student",Id(scope[0].Student)),("@program",Id(scope[0].Program)));
  var attempted=records.Sum(x=>x.AttemptedCredits);
  var earned=records.Sum(x=>x.EarnedCredits);
  var weighted=records.Sum(x=>x.WeightedPoints);
  var cgpa=attempted>0?Math.Round(weighted/attempted,2,MidpointRounding.AwayFromZero):0m;
  return new CollegeTranscript(enrollment,cgpa,attempted,earned,records.Sum(x=>x.Backlogs),records);
 }
 public async Task<CollegeReport> Report(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
    (SELECT COUNT(*) FROM college_programs WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM college_semesters WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM college_courses WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM college_courses WHERE tenant_id=@t AND campus_id=@c AND course_type='ELECTIVE'),
    (SELECT COUNT(*) FROM college_registrations WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM college_registrations WHERE tenant_id=@t AND campus_id=@c AND status='BACKLOG'),
    (SELECT COUNT(*) FROM college_semester_results WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COALESCE(AVG(sgpa),0) FROM college_semester_results WHERE tenant_id=@t AND campus_id=@c)
   """,db);Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var x=await cmd.ExecuteReaderAsync(ct);await x.ReadAsync(ct);
  return new CollegeReport(Convert.ToInt32(x.GetInt64(0)),Convert.ToInt32(x.GetInt64(1)),
   Convert.ToInt32(x.GetInt64(2)),Convert.ToInt32(x.GetInt64(3)),Convert.ToInt32(x.GetInt64(4)),
   Convert.ToInt32(x.GetInt64(5)),Convert.ToInt32(x.GetInt64(6)),
   Math.Round(x.GetDecimal(7),2,MidpointRounding.AwayFromZero));
 }
}
