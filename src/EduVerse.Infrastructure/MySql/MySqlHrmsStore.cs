using EduVerse.Application.Hrms;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlHrmsStore(string connectionString):IHrmsStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid g)=>g.ToString("D");
 private static DateTime Day(DateOnly d)=>d.ToDateTime(TimeOnly.MinValue);
 private static void Add(MySqlCommand cmd,params (string Key,object? Value)[] args)
 { foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value??DBNull.Value); }
 private static async Task<bool> Check(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,
   params (string Key,object? Value)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 private static Task<bool> Campus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)=>
  Check(db,tx,"SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1)",
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 private static Task<bool> Employee(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid employee,CancellationToken ct)=>
  Check(db,tx,"SELECT EXISTS(SELECT 1 FROM hr_employees WHERE tenant_id=@t AND campus_id=@c AND id=@e AND status<>'EXITED')",
   ct,("@t",Id(tenant)),("@c",Id(campus)),("@e",Id(employee)));
 private static async Task LockTenant(MySqlConnection db,MySqlTransaction tx,Guid tenant,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("SELECT id FROM tenants WHERE id=@tenant AND is_active=1 FOR UPDATE",db,tx);
  Add(cmd,("@tenant",Id(tenant)));
  if(await cmd.ExecuteScalarAsync(ct) is null)throw new HrScopeException();
 }
 public async Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
    JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
    JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
    JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
      AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
    JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='hrms'
    JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
    WHERE m.tenant_id=@tenant AND m.subject_id=@user AND m.is_active=1 AND m.role_code='ADMIN')
   """,db);
  Add(cmd,("@tenant",Id(actor.TenantId)),("@user",actor.SubjectId));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 public async Task<HrDesignation?> Designate(Guid tenant,HrDesignationCreate input,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,input.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO hr_designations(id,tenant_id,campus_id,code,title)
   VALUES(@id,@t,@c,@code,@title)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(input.CampusId)),
   ("@code",input.Code),("@title",input.Title));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new HrDesignation(id,input.CampusId,input.Code,input.Title);
 }
 public async Task<IReadOnlyList<HrDesignation>> Designations(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<HrDesignation>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,code,title FROM hr_designations
   WHERE tenant_id=@t AND campus_id=@c AND is_active=1 ORDER BY title LIMIT 200
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new HrDesignation(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3)));
  return list;
 }
 private static HrEmployee ReadEmployee(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),
   r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),
   r.IsDBNull(7)?null:Guid.Parse(r.GetString(7)),DateOnly.FromDateTime(r.GetDateTime(8)),
   r.IsDBNull(9)?null:r.GetGuid(9),r.IsDBNull(10)?null:r.GetGuid(10));
 public async Task<HrEmployee?> Hire(Guid tenant,string actor,HrEmployeeCreate input,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Campus(db,tx,tenant,input.CampusId,ct))return null;
  if(input.UserId is Guid user && !await Check(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m JOIN identity_users u
    ON u.id=m.subject_id AND u.is_active=1
    WHERE m.tenant_id=@t AND m.subject_id=@user AND m.is_active=1)
   """,ct,("@t",Id(tenant)),("@user",Id(user))))return null;
  if(input.DepartmentId is Guid department && !await Check(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM institution_departments WHERE id=@id
     AND tenant_id=@t AND campus_id=@c AND is_active=1)
   """,ct,("@id",Id(department)),("@t",Id(tenant)),("@c",Id(input.CampusId))))return null;
  if(input.DesignationId is Guid designation && !await Check(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM hr_designations WHERE id=@id AND tenant_id=@t
     AND campus_id=@c AND is_active=1)
   """,ct,("@id",Id(designation)),("@t",Id(tenant)),("@c",Id(input.CampusId))))return null;
  var id=Guid.NewGuid();var number="HRE-"+id.ToString("N").ToUpperInvariant();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO hr_employees(id,tenant_id,campus_id,employee_number,user_subject_id,given_name,
    family_name,work_email,joined_on,employee_type,department_id,designation_id)
   VALUES(@id,@t,@c,@number,@user,@first,@last,@email,@joined,@type,@department,@designation)
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(input.CampusId)),
    ("@number",number),("@user",input.UserId?.ToString("D")),("@first",input.GivenName),
    ("@last",input.FamilyName),("@email",input.WorkEmail),("@joined",Day(input.JoinedOn)),
    ("@type",input.Type),("@department",input.DepartmentId?.ToString("D")),
    ("@designation",input.DesignationId?.ToString("D")));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await using(var evt=new MySqlCommand("""
   INSERT INTO hr_employee_events(id,tenant_id,campus_id,employee_id,event_type,actor_subject_id)
   VALUES(@id,@t,@c,@employee,'CREATED',@actor)
   """,db,tx))
  {
   Add(evt,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(input.CampusId)),
     ("@employee",Id(id)),("@actor",actor));
   await evt.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new HrEmployee(id,input.CampusId,number,input.GivenName,input.FamilyName,input.Type,
   "ONBOARDING",input.UserId,input.JoinedOn,input.DepartmentId,input.DesignationId);
 }
 public async Task<IReadOnlyList<HrEmployee>> Employees(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<HrEmployee>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,employee_number,given_name,family_name,employee_type,status,
    user_subject_id,joined_on,department_id,designation_id
   FROM hr_employees WHERE tenant_id=@t AND campus_id=@c
   ORDER BY created_utc DESC LIMIT 250
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(ReadEmployee(r));
  return list;
 }
 public async Task<bool> Lifecycle(Guid tenant,string actor,Guid campus,Guid employee,HrLifecycle input,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  string? current;
  await using(var gate=new MySqlCommand("""
   SELECT status FROM hr_employees WHERE id=@e AND tenant_id=@t AND campus_id=@c FOR UPDATE
   """,db,tx))
  {
   Add(gate,("@e",Id(employee)),("@t",Id(tenant)),("@c",Id(campus)));
   current=await gate.ExecuteScalarAsync(ct) as string;
  }
  if(current is null)return false;
  var valid=(current,input.Status) switch{
   ("ONBOARDING","ACTIVE")=>true,("ACTIVE","ON_LEAVE")=>true,
   ("ON_LEAVE","ACTIVE")=>true,_=>false};
  if(!valid)throw new HrConflictException("Employment transition is not permitted.");
  await using(var cmd=new MySqlCommand("""
   UPDATE hr_employees SET status=@status,
     exited_on=IF(@status='EXITED',UTC_DATE(),exited_on)
   WHERE tenant_id=@t AND campus_id=@c AND id=@e
   """,db,tx))
  {
   Add(cmd,("@status",input.Status),("@t",Id(tenant)),("@c",Id(campus)),("@e",Id(employee)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  var ev=input.Status switch{"ACTIVE" when current=="ONBOARDING"=>"ACTIVATED",
   "ACTIVE"=>"RETURNED","ON_LEAVE"=>"ON_LEAVE",_=>"EXITED"};
  await using(var cmd=new MySqlCommand("""
   INSERT INTO hr_employee_events(id,tenant_id,campus_id,employee_id,event_type,note,actor_subject_id)
   VALUES(@id,@t,@c,@employee,@event,@note,@actor)
   """,db,tx))
  {
   Add(cmd,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(campus)),
     ("@employee",Id(employee)),("@event",ev),("@note",input.Note),("@actor",actor));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);return true;
 }
}
