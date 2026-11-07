using EduVerse.Application.College;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlCollegeStore(string connectionString):ICollegeStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid x)=>x.ToString("D");
 private static void Add(MySqlCommand cmd,params (string,object?)[] args)
 {foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v??DBNull.Value);}
 private static async Task<object?> Scalar(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,params (string,object?)[] args)
 {await using var c=new MySqlCommand(sql,db,tx);Add(c,args);return await c.ExecuteScalarAsync(ct);}
 private static async Task<int> Exec(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,params (string,object?)[] args)
 {await using var c=new MySqlCommand(sql,db,tx);Add(c,args);return await c.ExecuteNonQueryAsync(ct);}
 private static async Task<List<T>> Query<T>(MySqlConnection db,MySqlTransaction? tx,string sql,
  Func<MySqlDataReader,T> map,CancellationToken ct,params (string,object?)[] args)
 {
  var values=new List<T>();await using var c=new MySqlCommand(sql,db,tx);Add(c,args);
  await using var rd=await c.ExecuteReaderAsync(ct);
  while(await rd.ReadAsync(ct))values.Add(map(rd));
  return values;
 }
 private static async Task<bool> Exists(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,
  params (string,object?)[] args)=>Convert.ToInt32(await Scalar(db,tx,sql,ct,args))==1;
 private static async Task LockTenant(MySqlConnection db,MySqlTransaction tx,Guid tenant,CancellationToken ct)
 {if(await Scalar(db,tx,"SELECT id FROM tenants WHERE id=@t AND is_active=1 FOR UPDATE",ct,("@t",Id(tenant))) is null)
   throw new CollegeScopeException();}
 private static Task<bool> Campus(MySqlConnection db,MySqlTransaction? tx,Guid tenant,Guid campus,CancellationToken ct)=>
  Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1)",
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 public async Task<bool> IsAcademicAdmin(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Exists(db,null,"""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
    JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
    JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
    JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
      AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
    JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='academics'
    JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
    WHERE m.tenant_id=@t AND m.subject_id=@actor AND m.is_active=1 AND m.role_code='ADMIN')
   """,ct,("@t",Id(actor.TenantId)),("@actor",actor.SubjectId));
 }
 public async Task<CollegeRegulation?> CreateRegulation(Guid tenant,CollegeRegulationCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO college_regulations(id,tenant_id,campus_id,code,name,passing_points,
     min_credits_for_progression,allow_backlogs,max_semesters)
   VALUES(@id,@t,@c,@code,@name,@points,@min,@backlogs,@max)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@code",r.Code),("@name",r.Name),
   ("@points",r.PassingPoints),("@min",r.MinCreditsForProgression),("@backlogs",r.AllowBacklogs),("@max",r.MaxSemesters));
  await tx.CommitAsync(ct);
  return new CollegeRegulation(id,r.CampusId,r.Code,r.Name,r.PassingPoints,r.MinCreditsForProgression,r.AllowBacklogs,r.MaxSemesters);
 }
 public async Task<IReadOnlyList<CollegeRegulation>> Regulations(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,campus_id,code,name,passing_points,min_credits_for_progression,allow_backlogs,max_semesters
   FROM college_regulations WHERE tenant_id=@t AND campus_id=@c ORDER BY code LIMIT 200
   """,x=>new CollegeRegulation(x.GetGuid(0),x.GetGuid(1),x.GetString(2),x.GetString(3),
   x.GetDecimal(4),x.GetDecimal(5),x.GetBoolean(6),x.GetInt32(7)),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<CollegeProgram?> CreateProgram(Guid tenant,CollegeProgramCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM college_regulations reg
    JOIN academic_catalog item ON item.tenant_id=reg.tenant_id AND item.campus_id=reg.campus_id
     AND item.id=@catalog AND item.kind='PROGRAM' AND item.is_active=1
    WHERE reg.tenant_id=@t AND reg.campus_id=@c AND reg.id=@reg)
   """,ct,("@catalog",Id(r.CatalogProgramId)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@reg",Id(r.RegulationId))))
   return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO college_programs(id,tenant_id,campus_id,catalog_program_id,regulation_id,code,name)
   VALUES(@id,@t,@c,@catalog,@reg,@code,@name)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@catalog",Id(r.CatalogProgramId)),
    ("@reg",Id(r.RegulationId)),("@code",r.Code),("@name",r.Name));
  await tx.CommitAsync(ct);return new CollegeProgram(id,r.CampusId,r.CatalogProgramId,r.RegulationId,r.Code,r.Name);
 }
 public async Task<IReadOnlyList<CollegeProgram>> Programs(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,campus_id,catalog_program_id,regulation_id,code,name FROM college_programs
   WHERE tenant_id=@t AND campus_id=@c ORDER BY code LIMIT 200
   """,x=>new CollegeProgram(x.GetGuid(0),x.GetGuid(1),x.GetGuid(2),x.GetGuid(3),
     x.GetString(4),x.GetString(5)),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<CollegeSemester?> CreateSemester(Guid tenant,CollegeSemesterCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM college_programs p
    JOIN college_regulations reg ON reg.id=p.regulation_id AND reg.tenant_id=p.tenant_id
     AND reg.campus_id=p.campus_id
    JOIN academic_years y ON y.tenant_id=p.tenant_id AND y.campus_id=p.campus_id AND y.id=@year AND y.is_active=1
    JOIN academic_catalog sem ON sem.tenant_id=p.tenant_id AND sem.campus_id=p.campus_id
     AND sem.id=@catalog AND sem.kind='SEMESTER' AND sem.is_active=1
     AND sem.academic_year_id=y.id
    WHERE p.tenant_id=@t AND p.campus_id=@c AND p.id=@program AND @number<=reg.max_semesters)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@program",Id(r.ProgramId)),
    ("@year",Id(r.AcademicYearId)),("@catalog",Id(r.CatalogSemesterId)),("@number",r.Number)))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO college_semesters(id,tenant_id,campus_id,program_id,academic_year_id,catalog_semester_id,semester_number)
   VALUES(@id,@t,@c,@program,@year,@catalog,@number)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@program",Id(r.ProgramId)),
    ("@year",Id(r.AcademicYearId)),("@catalog",Id(r.CatalogSemesterId)),("@number",r.Number));
  await tx.CommitAsync(ct);
  return new CollegeSemester(id,r.ProgramId,r.AcademicYearId,r.CatalogSemesterId,r.Number,"OPEN");
 }
 public async Task<IReadOnlyList<CollegeSemester>> Semesters(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,program_id,academic_year_id,catalog_semester_id,semester_number,status
   FROM college_semesters WHERE tenant_id=@t AND campus_id=@c ORDER BY semester_number LIMIT 200
   """,x=>new CollegeSemester(x.GetGuid(0),x.GetGuid(1),x.GetGuid(2),
    x.GetGuid(3),x.GetInt32(4),x.GetString(5)),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<CollegeCourse?> CreateCourse(Guid tenant,CollegeCourseCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM college_semesters sem
    JOIN academic_catalog sub ON sub.tenant_id=sem.tenant_id AND sub.campus_id=sem.campus_id
     AND sub.id=@subject AND sub.kind='SUBJECT' AND sub.is_active=1
    WHERE sem.id=@semester AND sem.program_id=@program AND sem.tenant_id=@t
     AND sem.campus_id=@c AND sem.status='OPEN')
   """,ct,("@semester",Id(r.SemesterId)),("@program",Id(r.ProgramId)),("@subject",Id(r.SubjectId)),
     ("@t",Id(tenant)),("@c",Id(r.CampusId))))return null;
  if(r.PrerequisiteCourseId is Guid prereq && !await Exists(db,tx,"""
    SELECT EXISTS(SELECT 1 FROM college_courses previous
      JOIN college_semesters prior ON prior.id=previous.semester_id
        AND prior.tenant_id=previous.tenant_id AND prior.campus_id=previous.campus_id
      JOIN college_semesters currentsem ON currentsem.id=@semester AND currentsem.tenant_id=previous.tenant_id
        AND currentsem.campus_id=previous.campus_id
      WHERE previous.id=@pre AND previous.program_id=@program AND previous.tenant_id=@t
        AND previous.campus_id=@c AND prior.semester_number<currentsem.semester_number)
   """,ct,("@pre",Id(prereq)),("@semester",Id(r.SemesterId)),("@program",Id(r.ProgramId)),
    ("@t",Id(tenant)),("@c",Id(r.CampusId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO college_courses(id,tenant_id,campus_id,program_id,semester_id,subject_id,
     code,title,course_type,credits,capacity,prerequisite_course_id)
   VALUES(@id,@t,@c,@program,@semester,@subject,@code,@title,@type,@credits,@capacity,@pre)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@program",Id(r.ProgramId)),
   ("@semester",Id(r.SemesterId)),("@subject",Id(r.SubjectId)),("@code",r.Code),("@title",r.Title),
   ("@type",r.Type),("@credits",r.Credits),("@capacity",r.Capacity),("@pre",r.PrerequisiteCourseId?.ToString("D")));
  await tx.CommitAsync(ct);
  return new CollegeCourse(id,r.ProgramId,r.SemesterId,r.SubjectId,r.Code,r.Title,r.Type,r.Credits,r.Capacity,r.PrerequisiteCourseId);
 }
 public async Task<IReadOnlyList<CollegeCourse>> Courses(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,program_id,semester_id,subject_id,code,title,course_type,credits,capacity,prerequisite_course_id
   FROM college_courses WHERE tenant_id=@t AND campus_id=@c ORDER BY code LIMIT 500
   """,x=>new CollegeCourse(x.GetGuid(0),x.GetGuid(1),x.GetGuid(2),x.GetGuid(3),
    x.GetString(4),x.GetString(5),x.GetString(6),x.GetDecimal(7),x.GetInt32(8),
    x.IsDBNull(9)?null:x.GetGuid(9)),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
}
