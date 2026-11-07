using EduVerse.Application.Exams;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlExamStore(string connectionString):IExamStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid g)=>g.ToString("D");
 private static void Add(MySqlCommand command,params (string Name,object? Value)[] args)
 {
  foreach(var (name,value) in args)command.Parameters.AddWithValue(name,value??DBNull.Value);
 }
 private static DateTime Date(DateOnly value)=>value.ToDateTime(TimeOnly.MinValue);
 private static async Task<bool> Exists(MySqlConnection db,MySqlTransaction tx,string sql,CancellationToken ct,
    params (string Name,object? Value)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 private static async Task<bool> Campus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)
  => await Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1)",
   ct,("@t",Id(tenant)),("@c",Id(campus)));

 public async Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
    JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
    JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
    JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
      AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
    JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='academics'
    JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
    WHERE m.tenant_id=@tenant AND m.subject_id=@user AND m.is_active=1 AND m.role_code='ADMIN')
   """,db);
  Add(cmd,("@tenant",Id(actor.TenantId)),("@user",actor.SubjectId));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }

 public async Task<GradingSchemeRecord?> CreateSchemeAsync(Guid tenant,GradingSchemeCreate request,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,request.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using(var insert=new MySqlCommand("""
   INSERT INTO exam_grading_schemes(id,tenant_id,campus_id,code,name)
   VALUES(@id,@t,@c,@code,@name)
   """,db,tx))
  {
   Add(insert,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(request.CampusId)),
    ("@code",request.Code),("@name",request.Name));await insert.ExecuteNonQueryAsync(ct);
  }
  foreach(var band in request.Bands)
  {
   await using var insert=new MySqlCommand("""
    INSERT INTO exam_grade_bands(id,tenant_id,campus_id,scheme_id,grade,min_percent,max_percent,grade_points)
    VALUES(@id,@t,@c,@scheme,@grade,@low,@high,@points)
    """,db,tx);
   Add(insert,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(request.CampusId)),
    ("@scheme",Id(id)),("@grade",band.Grade),("@low",band.MinPercent),
    ("@high",band.MaxPercent),("@points",band.Points));
   await insert.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new GradingSchemeRecord(id,request.CampusId,request.Code,request.Name,request.Bands);
 }
 public async Task<IReadOnlyList<GradingSchemeRecord>> SchemesAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<GradingSchemeRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT s.id,s.campus_id,s.code,s.name,b.grade,b.min_percent,b.max_percent,b.grade_points
   FROM exam_grading_schemes s
   JOIN exam_grade_bands b ON b.tenant_id=s.tenant_id AND b.campus_id=s.campus_id AND b.scheme_id=s.id
   WHERE s.tenant_id=@t AND s.campus_id=@c AND s.is_active=1
   ORDER BY s.code,b.min_percent LIMIT 500
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  var groups=new Dictionary<Guid,(Guid Campus,string Code,string Name,List<GradeBandCreate> Bands)>();
  while(await r.ReadAsync(ct))
  {
   var id=r.GetGuid(0);
   if(!groups.TryGetValue(id,out var group))
   {
    group=(r.GetGuid(1),r.GetString(2),r.GetString(3),new List<GradeBandCreate>());
    groups[id]=group;
   }
   group.Bands.Add(new GradeBandCreate(r.GetString(4),r.GetDecimal(5),r.GetDecimal(6),r.GetDecimal(7)));
  }
  foreach(var (id,g) in groups)list.Add(new GradingSchemeRecord(id,g.Campus,g.Code,g.Name,g.Bands));
  return list;
 }

 public async Task<ExamRecord?> CreateExamAsync(Guid tenant,ExamCreate request,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,request.CampusId,ct))return null;
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM academic_years y JOIN exam_grading_schemes s
    ON s.tenant_id=y.tenant_id AND s.campus_id=y.campus_id AND s.id=@scheme AND s.is_active=1
    WHERE y.id=@year AND y.tenant_id=@t AND y.campus_id=@c AND y.is_active=1)
   """,ct,("@scheme",Id(request.SchemeId)),("@year",Id(request.AcademicYearId)),
    ("@t",Id(tenant)),("@c",Id(request.CampusId))))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO examinations(id,tenant_id,campus_id,academic_year_id,scheme_id,code,title,exam_type)
   VALUES(@id,@t,@c,@year,@scheme,@code,@title,@type)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(request.CampusId)),
   ("@year",Id(request.AcademicYearId)),("@scheme",Id(request.SchemeId)),
   ("@code",request.Code),("@title",request.Title),("@type",request.Type));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new ExamRecord(id,request.CampusId,request.AcademicYearId,request.SchemeId,
   request.Code,request.Title,request.Type,"DRAFT");
 }
 public async Task<IReadOnlyList<ExamRecord>> ExamsAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<ExamRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,academic_year_id,scheme_id,code,title,exam_type,status
   FROM examinations WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 200
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new ExamRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),
   r.GetGuid(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7)));
  return list;
 }
}
