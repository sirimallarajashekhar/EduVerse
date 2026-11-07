using EduVerse.Application.Ams;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlAmsStore
{
 private static async Task<string?> LockStatus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid asset,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT status FROM ams_assets WHERE tenant_id=@t AND campus_id=@c AND id=@asset FOR UPDATE
   """,db,tx);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@asset",Id(asset)));
  return await cmd.ExecuteScalarAsync(ct) as string;
 }
 private static async Task<bool> ValidTarget(MySqlConnection db,MySqlTransaction tx,
   Guid tenant,Guid campus,Guid? building,Guid? department,Guid? employee,CancellationToken ct)
 {
  if(building is Guid b && !await Exists(db,tx,"""
    SELECT EXISTS(SELECT 1 FROM institution_buildings
      WHERE tenant_id=@t AND campus_id=@c AND id=@id AND is_active=1)
    """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(b))))return false;
  if(department is Guid d && !await Exists(db,tx,"""
    SELECT EXISTS(SELECT 1 FROM institution_departments
      WHERE tenant_id=@t AND campus_id=@c AND id=@id AND is_active=1)
    """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(d))))return false;
  if(employee is Guid e && !await Exists(db,tx,"""
    SELECT EXISTS(SELECT 1 FROM hr_employees
      WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status IN ('ACTIVE','ON_LEAVE'))
    """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(e))))return false;
  return true;
 }
 public async Task<AmsAsset?> Allocate(Guid tenant,string actor,AmsAllocation r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  var state=await LockStatus(db,tx,tenant,r.CampusId,r.AssetId,ct);
  if(state is null)return null;
  if(state is not ("AVAILABLE" or "ALLOCATED"))
   throw new AmsConflictException("Only available/allocated assets may be assigned.");
  if(r.BuildingId is null&&r.DepartmentId is null&&r.EmployeeId is null)
   throw new AmsConflictException("Provide at least one building, department or employee.");
  if(!await ValidTarget(db,tx,tenant,r.CampusId,r.BuildingId,r.DepartmentId,r.EmployeeId,ct))
   return null;
  await using(var cmd=new MySqlCommand("""
   UPDATE ams_assets SET status='ALLOCATED',building_id=@building,department_id=@department,
    employee_id=@employee WHERE id=@asset AND tenant_id=@t AND campus_id=@c
   """,db,tx))
  {
   Add(cmd,("@building",r.BuildingId?.ToString("D")),("@department",r.DepartmentId?.ToString("D")),
    ("@employee",r.EmployeeId?.ToString("D")),("@asset",Id(r.AssetId)),
    ("@t",Id(tenant)),("@c",Id(r.CampusId)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await Event(db,tx,tenant,r.CampusId,r.AssetId,
   state=="ALLOCATED"?"TRANSFERRED":"ALLOCATED",r.Note,actor,ct);
  await tx.CommitAsync(ct);
  return await ReadAsset(db,null,tenant,r.CampusId,r.AssetId,ct);
 }
 public async Task<AmsAsset?> Lifecycle(Guid tenant,string actor,AmsAssetAction r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  var state=await LockStatus(db,tx,tenant,r.CampusId,r.AssetId,ct);
  if(state is null)return null;
  var target=(state,r.Action) switch
  {
   ("ALLOCATED","RETURN")=>"AVAILABLE",
   ("AVAILABLE","MAINTENANCE")=>"MAINTENANCE",
   ("ALLOCATED","MAINTENANCE")=>"MAINTENANCE",
   ("MAINTENANCE","REPAIR")=>"AVAILABLE",
   ("AVAILABLE","RETIRE")=>"RETIRED",
   ("RETIRED","DISPOSE")=>"DISPOSED",
   _=>throw new AmsConflictException("Invalid asset lifecycle transition.")
  };
  await using(var cmd=new MySqlCommand("""
   UPDATE ams_assets SET status=@status,
    building_id=IF(@clear=1,NULL,building_id),
    department_id=IF(@clear=1,NULL,department_id),
    employee_id=IF(@clear=1,NULL,employee_id)
   WHERE id=@asset AND tenant_id=@t AND campus_id=@c
   """,db,tx))
  {
   Add(cmd,("@status",target),("@clear",r.Action is "RETURN" or "REPAIR" or "RETIRE"?1:0),
     ("@asset",Id(r.AssetId)),("@t",Id(tenant)),("@c",Id(r.CampusId)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await Event(db,tx,tenant,r.CampusId,r.AssetId,r.Action switch
   {"RETURN"=>"RETURNED","REPAIR"=>"REPAIRED","RETIRE"=>"RETIRED","DISPOSE"=>"DISPOSED",_=>"MAINTENANCE"},
   r.Note,actor,ct);
  await tx.CommitAsync(ct);
  return await ReadAsset(db,null,tenant,r.CampusId,r.AssetId,ct);
 }
 public async Task<AmsWorkOrder?> CreateWorkOrder(Guid tenant,AmsMaintenanceCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  var status=await LockStatus(db,tx,tenant,r.CampusId,r.AssetId,ct);
  if(status is null)return null;
  if(status is "RETIRED" or "DISPOSED")throw new AmsConflictException("Retired or disposed assets cannot receive work orders.");
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO ams_maintenance(id,tenant_id,campus_id,asset_id,title,due_on)
   VALUES(@id,@t,@c,@asset,@title,@due)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@asset",Id(r.AssetId)),("@title",r.Title),("@due",r.DueOn.ToDateTime(TimeOnly.MinValue)));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new AmsWorkOrder(id,r.AssetId,r.Title,r.DueOn,"OPEN");
 }
 public async Task<IReadOnlyList<AmsWorkOrder>> WorkOrders(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<AmsWorkOrder>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,asset_id,title,due_on,status FROM ams_maintenance WHERE tenant_id=@t AND campus_id=@c
   ORDER BY due_on DESC LIMIT 500
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))list.Add(new AmsWorkOrder(reader.GetGuid(0),reader.GetGuid(1),
   reader.GetString(2),DateOnly.FromDateTime(reader.GetDateTime(3)),reader.GetString(4)));
  return list;
 }
 public async Task<bool> CompleteWorkOrder(Guid tenant,string actor,Guid campus,Guid workOrder,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  Guid asset;
  await using(var find=new MySqlCommand("""
   SELECT asset_id FROM ams_maintenance WHERE tenant_id=@t AND campus_id=@c
    AND id=@order AND status='OPEN' FOR UPDATE
   """,db,tx))
  {
   Add(find,("@t",Id(tenant)),("@c",Id(campus)),("@order",Id(workOrder)));
   var value=await find.ExecuteScalarAsync(ct);
   if(value is null)return false;
   asset=value is Guid g?g:Guid.Parse(Convert.ToString(value)!);
  }
  await using(var cmd=new MySqlCommand("""
   UPDATE ams_maintenance SET status='COMPLETED',completed_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@order AND status='OPEN'
   """,db,tx))
  {
   Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@order",Id(workOrder)));
   if(await cmd.ExecuteNonQueryAsync(ct)!=1)return false;
  }
  await Event(db,tx,tenant,campus,asset,"REPAIRED","Work order completed",actor,ct);
  await tx.CommitAsync(ct);return true;
 }
}
