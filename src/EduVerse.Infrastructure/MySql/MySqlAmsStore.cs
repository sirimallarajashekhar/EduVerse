using EduVerse.Application.Ams;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlAmsStore(string connectionString):IAmsStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid id)=>id.ToString("D");
 private static void Add(MySqlCommand cmd,params (string Key,object? Value)[] parameters)
 {
  foreach(var (key,value) in parameters)cmd.Parameters.AddWithValue(key,value??DBNull.Value);
 }
 private static async Task<bool> Exists(MySqlConnection db,MySqlTransaction? tx,string sql,CancellationToken ct,
   params (string Key,object? Value)[] args)
 {
  await using var cmd=new MySqlCommand(sql,db,tx);Add(cmd,args);
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 private static Task<bool> Campus(MySqlConnection db,MySqlTransaction? tx,Guid tenant,Guid campus,CancellationToken ct)=>
  Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM campuses WHERE id=@c AND tenant_id=@t AND is_active=1)",
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 private static async Task LockTenant(MySqlConnection db,MySqlTransaction tx,Guid tenant,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("SELECT id FROM tenants WHERE id=@t AND is_active=1 FOR UPDATE",db,tx);
  Add(cmd,("@t",Id(tenant)));
  if(await cmd.ExecuteScalarAsync(ct) is null)throw new AmsScopeException();
 }
 public async Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
    JOIN identity_users u ON u.id=m.subject_id AND u.is_active=1
    JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
    JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
      AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
    JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='ams'
    JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
    WHERE m.tenant_id=@tenant AND m.subject_id=@actor AND m.is_active=1 AND m.role_code='ADMIN')
   """,db);
  Add(cmd,("@tenant",Id(actor.TenantId)),("@actor",actor.SubjectId));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 public async Task<AmsCategory?> AddCategory(Guid tenant,AmsCategoryCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO ams_categories(id,tenant_id,campus_id,code,display_name)
   VALUES(@id,@t,@c,@code,@name)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@code",r.Code),("@name",r.Name));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new AmsCategory(id,r.CampusId,r.Code,r.Name);
 }
 public async Task<IReadOnlyList<AmsCategory>> Categories(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<AmsCategory>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,code,display_name FROM ams_categories WHERE tenant_id=@t
    AND campus_id=@c ORDER BY code LIMIT 200
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new AmsCategory(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3)));
  return list;
 }
 public async Task<AmsVendor?> AddVendor(Guid tenant,AmsVendorCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO ams_vendors(id,tenant_id,campus_id,display_name,contact_email)
   VALUES(@id,@t,@c,@name,@email)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@name",r.Name),("@email",r.Email));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new AmsVendor(id,r.CampusId,r.Name,r.Email);
 }
 public async Task<IReadOnlyList<AmsVendor>> Vendors(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<AmsVendor>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,display_name,contact_email FROM ams_vendors
   WHERE tenant_id=@t AND campus_id=@c ORDER BY display_name LIMIT 200
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
   list.Add(new AmsVendor(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3)));
  return list;
 }
 private static AmsAsset Asset(MySqlDataReader r)=>new(r.GetGuid(0),r.GetGuid(1),r.GetString(2),
  r.GetGuid(3),r.GetString(4),r.GetString(5),r.IsDBNull(6)?null:r.GetGuid(6),
  r.IsDBNull(7)?null:r.GetGuid(7),r.IsDBNull(8)?null:r.GetGuid(8));
 private static async Task<AmsAsset?> ReadAsset(MySqlConnection db,MySqlTransaction? tx,Guid tenant,Guid campus,Guid asset,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,asset_code,category_id,display_name,status,building_id,department_id,employee_id
   FROM ams_assets WHERE tenant_id=@t AND campus_id=@c AND id=@asset
   """,db,tx);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@asset",Id(asset)));
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  return await reader.ReadAsync(ct)?Asset(reader):null;
 }
 public async Task<AmsAsset?> Register(Guid tenant,string actor,AmsAssetCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct)||
    !await Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM ams_categories WHERE tenant_id=@t AND campus_id=@c AND id=@category)",
      ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@category",Id(r.CategoryId))))return null;
  var id=Guid.NewGuid();var code="EVA-"+id.ToString("N").ToUpperInvariant();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO ams_assets(id,tenant_id,campus_id,asset_code,category_id,display_name,
      serial_number,acquired_on,purchase_amount)
   VALUES(@id,@t,@c,@code,@category,@name,@serial,@acquired,@cost)
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@code",code),
    ("@category",Id(r.CategoryId)),("@name",r.Name),("@serial",r.SerialNumber),
    ("@acquired",r.AcquiredOn?.ToDateTime(TimeOnly.MinValue)),("@cost",r.PurchaseAmount));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await Event(db,tx,tenant,r.CampusId,id,"REGISTERED",null,actor,ct);
  await tx.CommitAsync(ct);
  return new AmsAsset(id,r.CampusId,code,r.CategoryId,r.Name,"AVAILABLE",null,null,null);
 }
 public async Task<IReadOnlyList<AmsAsset>> Assets(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<AmsAsset>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,asset_code,category_id,display_name,status,building_id,department_id,employee_id
   FROM ams_assets WHERE tenant_id=@t AND campus_id=@c ORDER BY created_utc DESC LIMIT 500
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(Asset(r));
  return list;
 }
 private static async Task Event(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,
   Guid asset,string action,string? note,string actor,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   INSERT INTO ams_asset_events(id,tenant_id,campus_id,asset_id,event_code,note,actor_id)
   VALUES(@id,@t,@c,@asset,@action,@note,@actor)
   """,db,tx);
  Add(cmd,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(campus)),
   ("@asset",Id(asset)),("@action",action),("@note",note),("@actor",actor));
  await cmd.ExecuteNonQueryAsync(ct);
 }
 public async Task<IReadOnlyList<AmsAssetEvent>> AssetEvents(Guid tenant,Guid campus,Guid asset,CancellationToken ct)
 {
  var list=new List<AmsAssetEvent>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT asset_id,event_code,note,created_utc FROM ams_asset_events
   WHERE tenant_id=@t AND campus_id=@c AND asset_id=@asset ORDER BY created_utc DESC LIMIT 200
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@asset",Id(asset)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new AmsAssetEvent(r.GetGuid(0),r.GetString(1),
   r.IsDBNull(2)?null:r.GetString(2),DateTime.SpecifyKind(r.GetDateTime(3),DateTimeKind.Utc)));
  return list;
 }
}
