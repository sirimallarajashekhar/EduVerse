using EduVerse.Application.OperationsModules;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlOperationsStore(string connectionString):IOperationsStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid x)=>x.ToString("D");
 private static DateTime Day(DateOnly d)=>d.ToDateTime(TimeOnly.MinValue);
 private static void Add(MySqlCommand cmd,params (string,object?)[] values)
 {foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value??DBNull.Value);}
 private static async Task<object?> Scalar(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,params (string,object?)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);return await cmd.ExecuteScalarAsync(ct);
 }
 private static async Task<int> Exec(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,params (string,object?)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);return await cmd.ExecuteNonQueryAsync(ct);
 }
 private static async Task<List<T>> Query<T>(MySqlConnection db,MySqlTransaction? tx,string sql,
    Func<MySqlDataReader,T> map,CancellationToken ct,params (string,object?)[] args)
 {
  var result=new List<T>();
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))result.Add(map(reader));
  return result;
 }
 private static async Task<bool> Exists(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,params (string,object?)[] args)
  =>Convert.ToInt32(await Scalar(db,tx,sql,ct,args))==1;
 private static Task<bool> Campus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)
  =>Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM campuses WHERE tenant_id=@t AND id=@c AND is_active=1)",ct,("@t",Id(tenant)),("@c",Id(campus)));
 private static async Task LockTenant(MySqlConnection db,MySqlTransaction tx,Guid tenant,CancellationToken ct)
 {
  if(await Scalar(db,tx,"SELECT id FROM tenants WHERE id=@t AND is_active=1 FOR UPDATE",ct,("@t",Id(tenant))) is null)
   throw new OperationsScopeException();
 }
 private static Task<bool> Student(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid student,CancellationToken ct)
  =>Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM students WHERE tenant_id=@t AND campus_id=@c AND id=@s AND status='ACTIVE')",
   ct,("@t",Id(tenant)),("@c",Id(campus)),("@s",Id(student)));
 public async Task<bool> Admin(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Exists(db,null,"""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
    JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
    JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
    JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
      AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
    JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='core'
    JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
    WHERE m.tenant_id=@t AND m.subject_id=@actor AND m.role_code='ADMIN' AND m.is_active=1)
   """,ct,("@t",Id(actor.TenantId)),("@actor",actor.SubjectId));
 }
}
