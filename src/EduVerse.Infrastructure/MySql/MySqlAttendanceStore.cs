using EduVerse.Application.Attendance;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlAttendanceStore(string connectionString):IAttendanceStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid id)=>id.ToString("D");
 private static void Add(MySqlCommand command,params (string Key,object? Value)[] args)
 {
  foreach(var (key,value) in args)command.Parameters.AddWithValue(key,value??DBNull.Value);
 }
 private static DateTime Date(DateOnly date)=>date.ToDateTime(TimeOnly.MinValue);

 public async Task<bool> IsActiveAdminAsync(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
     JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
     JOIN identity_users u ON u.id=@user AND u.is_active=1
     JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
        AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
     JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='academics'
     JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
     WHERE m.tenant_id=@tenant AND m.subject_id=@user AND m.is_active=1 AND m.role_code='ADMIN')
   """,db);
  Add(cmd,("@tenant",Id(actor.TenantId)),("@user",actor.SubjectId));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 private static async Task<bool> ActiveCampus(MySqlConnection db,MySqlTransaction? tx,Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM campuses WHERE id=@campus AND tenant_id=@tenant AND is_active=1)
   """,db,tx);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 private static async Task LockCampus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT id FROM campuses WHERE id=@campus AND tenant_id=@tenant AND is_active=1 FOR UPDATE
   """,db,tx);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  if(await cmd.ExecuteScalarAsync(ct) is null)throw new AttendanceScopeException();
 }
 private static async Task<bool> Holiday(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,DateOnly date,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM academic_calendar_events
   WHERE tenant_id=@tenant AND campus_id=@campus AND kind='HOLIDAY'
     AND starts_on<=@date AND ends_on>=@date)
   """,db,tx);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@date",Date(date)));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 public async Task<PeriodRecord?> CreatePeriodAsync(Guid tenant,PeriodCreate request,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await ActiveCampus(db,tx,tenant,request.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO academic_periods(id,tenant_id,campus_id,code,display_name,starts_at,ends_at)
   VALUES(@id,@tenant,@campus,@code,@name,@start,@end)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(request.CampusId)),
   ("@code",request.Code),("@name",request.Name),
   ("@start",request.StartsAt.ToTimeSpan()),("@end",request.EndsAt.ToTimeSpan()));
  await cmd.ExecuteNonQueryAsync(ct);
  await tx.CommitAsync(ct);
  return new PeriodRecord(id,request.CampusId,request.Code,request.Name,request.StartsAt,request.EndsAt,true);
 }
 public async Task<IReadOnlyList<PeriodRecord>> PeriodsAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  var result=new List<PeriodRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,code,display_name,starts_at,ends_at,is_active FROM academic_periods
   WHERE tenant_id=@tenant AND campus_id=@campus ORDER BY starts_at LIMIT 100
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
   result.Add(new PeriodRecord(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3),
    TimeOnly.FromTimeSpan(r.GetTimeSpan(4)),TimeOnly.FromTimeSpan(r.GetTimeSpan(5)),r.GetBoolean(6)));
  return result;
 }
}
