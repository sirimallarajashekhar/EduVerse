using EduVerse.Application.College;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlCollegeStore
{
 private static CollegeRegistration MapRegistration(MySqlDataReader x)=>
  new(x.GetGuid(0),x.GetGuid(1),x.GetGuid(2),x.GetGuid(3),x.GetString(4),
   x.IsDBNull(5)?null:x.GetDecimal(5),x.IsDBNull(6)?null:x.GetString(6),
   x.IsDBNull(7)?null:x.GetBoolean(7));

 public async Task<CollegeRegistration?> Register(Guid tenant,CollegeRegistrationCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  var rows=await Query(db,tx,"""
   SELECT c.semester_id,c.capacity,c.prerequisite_course_id,
    sem.program_id,sem.semester_number,e.student_id
   FROM college_courses c
   JOIN college_semesters sem ON sem.tenant_id=c.tenant_id AND sem.campus_id=c.campus_id
     AND sem.id=c.semester_id AND sem.status='OPEN'
   JOIN college_programs p ON p.tenant_id=c.tenant_id AND p.campus_id=c.campus_id AND p.id=sem.program_id
   JOIN student_enrollments e ON e.tenant_id=c.tenant_id AND e.campus_id=c.campus_id
     AND e.id=@enrollment AND e.academic_year_id=sem.academic_year_id AND e.status='ACTIVE'
   JOIN students st ON st.id=e.student_id AND st.tenant_id=e.tenant_id AND st.campus_id=e.campus_id AND st.status='ACTIVE'
   JOIN academic_catalog eitem ON eitem.tenant_id=e.tenant_id AND eitem.campus_id=e.campus_id
     AND eitem.id=e.academic_item_id AND eitem.kind='COURSE' AND eitem.parent_id=p.catalog_program_id
   WHERE c.tenant_id=@t AND c.campus_id=@c AND c.id=@course
   """,x=>(Semester:x.GetGuid(0),Capacity:x.GetInt32(1),
      Prerequisite:x.IsDBNull(2)?(Guid?)null:x.GetGuid(2),
      Program:x.GetGuid(3),Number:x.GetInt32(4),Student:x.GetGuid(5)),ct,
    ("@t",Id(tenant)),("@c",Id(r.CampusId)),("@enrollment",Id(r.EnrollmentId)),("@course",Id(r.CourseId)));
  if(rows.Count==0)return null;
  var row=rows[0];
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM college_registrations
    WHERE tenant_id=@t AND campus_id=@c AND enrollment_id=@enroll AND course_id=@course)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@enroll",Id(r.EnrollmentId)),
   ("@course",Id(r.CourseId))))
   throw new CollegeConflictException("Student is already registered for this course.");
  if(row.Prerequisite is Guid prerequisite && !await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM college_registrations prior
    JOIN student_enrollments e ON e.tenant_id=prior.tenant_id AND e.campus_id=prior.campus_id
      AND e.id=prior.enrollment_id AND e.student_id=@student
    WHERE prior.tenant_id=@t AND prior.campus_id=@c AND prior.course_id=@prereq
      AND prior.status='PASSED')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@student",Id(row.Student)),
   ("@prereq",Id(prerequisite))))
   throw new CollegeConflictException("Prerequisite course has not been passed.");
  // Semester progression is derived only from previous *finalized* results for this same program.
  if(row.Number>1)
  {
   var prior=await Scalar(db,tx,"""
    SELECT sr.progression_eligible FROM college_semester_results sr
    JOIN college_semesters sem ON sem.id=sr.semester_id AND sem.tenant_id=sr.tenant_id
       AND sem.campus_id=sr.campus_id AND sem.program_id=@program
    JOIN student_enrollments prior_en ON prior_en.id=sr.enrollment_id
       AND prior_en.tenant_id=sr.tenant_id AND prior_en.campus_id=sr.campus_id
       AND prior_en.student_id=@student
    WHERE sr.tenant_id=@t AND sr.campus_id=@c AND sem.semester_number=@previous
    ORDER BY sr.finalized_utc DESC LIMIT 1
    """,ct,("@program",Id(row.Program)),("@student",Id(row.Student)),("@previous",row.Number-1),
     ("@t",Id(tenant)),("@c",Id(r.CampusId)));
   if(prior is null||!Convert.ToBoolean(prior))
    throw new CollegeConflictException("Prior semester is not finalized as progression eligible.");
  }
  var seats=Convert.ToInt32(await Scalar(db,tx,"""
   SELECT COUNT(*) FROM college_registrations WHERE tenant_id=@t AND campus_id=@c AND course_id=@course
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@course",Id(r.CourseId))));
  if(seats>=row.Capacity)throw new CollegeConflictException("Course enrollment capacity reached.");
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO college_registrations(id,tenant_id,campus_id,enrollment_id,course_id,semester_id)
   VALUES(@id,@t,@c,@enroll,@course,@semester)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@enroll",Id(r.EnrollmentId)),("@course",Id(r.CourseId)),("@semester",Id(row.Semester)));
  await tx.CommitAsync(ct);
  return new CollegeRegistration(id,r.EnrollmentId,r.CourseId,row.Semester,"ENROLLED",null,null,null);
 }
 public async Task<IReadOnlyList<CollegeRegistration>> Registrations(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,enrollment_id,course_id,semester_id,status,grade_points,grade_label,passed
   FROM college_registrations WHERE tenant_id=@t AND campus_id=@c AND enrollment_id=@enroll
   ORDER BY created_utc LIMIT 300
   """,MapRegistration,ct,("@t",Id(tenant)),("@c",Id(campus)),("@enroll",Id(enrollment)));
 }
 public async Task<CollegeRegistration?> ImportGrade(Guid tenant,CollegeGradeFromExam r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  // Published exam subject snapshots, never manually entered points, are the sole grade source.
  var source=await Query(db,tx,"""
   SELECT ss.id,ss.grade_points,ss.grade,ss.passed,reg.status,reg.enrollment_id,
      reg.course_id,reg.semester_id
   FROM college_registrations reg
   JOIN college_courses course ON course.id=reg.course_id
     AND course.tenant_id=reg.tenant_id AND course.campus_id=reg.campus_id
   JOIN college_semesters sem ON sem.id=reg.semester_id
     AND sem.tenant_id=reg.tenant_id AND sem.campus_id=reg.campus_id AND sem.status='OPEN'
   JOIN exam_result_publications publication ON publication.tenant_id=reg.tenant_id
     AND publication.campus_id=reg.campus_id AND publication.enrollment_id=reg.enrollment_id
     AND publication.exam_id=@exam
   JOIN examinations exam ON exam.id=publication.exam_id AND exam.tenant_id=publication.tenant_id
     AND exam.campus_id=publication.campus_id AND exam.status='PUBLISHED'
     AND exam.exam_type IN ('SEMESTER','FINAL') AND exam.academic_year_id=sem.academic_year_id
   JOIN exam_result_subject_snapshots ss ON ss.result_id=publication.id
     AND ss.tenant_id=publication.tenant_id AND ss.campus_id=publication.campus_id
     AND ss.subject_id=course.subject_id
   WHERE reg.tenant_id=@t AND reg.campus_id=@c AND reg.id=@registration
   """,x=>(Snapshot:x.GetGuid(0),Points:x.GetDecimal(1),Label:x.GetString(2),
    Passed:x.GetBoolean(3),State:x.GetString(4),Enrollment:x.GetGuid(5),
    Course:x.GetGuid(6),Semester:x.GetGuid(7)),ct,
   ("@t",Id(tenant)),("@c",Id(r.CampusId)),("@registration",Id(r.RegistrationId)),("@exam",Id(r.ExamId)));
  if(source.Count==0)return null;
  var x=source[0];
  if(x.State!="ENROLLED")throw new CollegeConflictException("A posted grade cannot be overwritten; use a formal revaluation workflow.");
  var threshold=await Scalar(db,tx,"""
   SELECT rule.passing_points FROM college_courses course
   JOIN college_programs p ON p.id=course.program_id AND p.tenant_id=course.tenant_id AND p.campus_id=course.campus_id
   JOIN college_regulations rule ON rule.id=p.regulation_id AND rule.tenant_id=p.tenant_id AND rule.campus_id=p.campus_id
   WHERE course.tenant_id=@t AND course.campus_id=@c AND course.id=@course
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@course",Id(x.Course)));
  if(threshold is null)return null;
  var passed=x.Passed && x.Points>=Convert.ToDecimal(threshold);
  await Exec(db,tx,"""
   UPDATE college_registrations SET status=@state,grade_points=@points,
    grade_label=@grade,passed=@passed,source_snapshot_id=@source,graded_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@registration AND status='ENROLLED'
   """,ct,("@state",passed?"PASSED":"BACKLOG"),("@points",x.Points),("@grade",x.Label),
   ("@passed",passed),("@source",Id(x.Snapshot)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@registration",Id(r.RegistrationId)));
  await tx.CommitAsync(ct);
  return new CollegeRegistration(r.RegistrationId,x.Enrollment,x.Course,x.Semester,
    passed?"PASSED":"BACKLOG",x.Points,x.Label,passed);
 }
}
