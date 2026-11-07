using EduVerse.Application.OperationsModules;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlOperationsStore
{
 public async Task<HostelBlock?> Block(Guid tenant,HostelBlockCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO hostel_blocks(id,tenant_id,campus_id,code,name) VALUES(@id,@t,@c,@code,@name)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@code",r.Code),("@name",r.Name));
  await tx.CommitAsync(ct);return new HostelBlock(id,r.Code,r.Name);
 }
 public async Task<IReadOnlyList<HostelBlock>> Blocks(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,code,name FROM hostel_blocks WHERE tenant_id=@t AND campus_id=@c ORDER BY code LIMIT 200
   """,r=>new HostelBlock(r.GetGuid(0),r.GetString(1),r.GetString(2)),ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<HostelRoom?> Room(Guid tenant,HostelRoomCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM hostel_blocks WHERE tenant_id=@t AND campus_id=@c AND id=@block)",
    ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@block",Id(r.BlockId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO hostel_rooms(id,tenant_id,campus_id,block_id,room_number)
   VALUES(@id,@t,@c,@block,@number)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@block",Id(r.BlockId)),("@number",r.Number));
  await tx.CommitAsync(ct);return new HostelRoom(id,r.BlockId,r.Number);
 }
 public async Task<IReadOnlyList<HostelRoom>> Rooms(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,block_id,room_number FROM hostel_rooms WHERE tenant_id=@t AND campus_id=@c
   ORDER BY room_number LIMIT 500
   """,r=>new HostelRoom(r.GetGuid(0),r.GetGuid(1),r.GetString(2)),
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<HostelBed?> Bed(Guid tenant,HostelBedCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM hostel_rooms WHERE tenant_id=@t AND campus_id=@c AND id=@room)",
    ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@room",Id(r.RoomId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO hostel_beds(id,tenant_id,campus_id,room_id,bed_number) VALUES(@id,@t,@c,@room,@number)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@room",Id(r.RoomId)),("@number",r.Number));
  await tx.CommitAsync(ct);return new HostelBed(id,r.RoomId,r.Number,false);
 }
 public async Task<IReadOnlyList<HostelBed>> Beds(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT b.id,b.room_id,b.bed_number,EXISTS(SELECT 1 FROM hostel_allocations a
     WHERE a.tenant_id=b.tenant_id AND a.campus_id=b.campus_id
      AND a.bed_id=b.id AND a.status='ACTIVE')
   FROM hostel_beds b WHERE b.tenant_id=@t AND b.campus_id=@c
   ORDER BY b.room_id,b.bed_number LIMIT 1000
   """,r=>new HostelBed(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetBoolean(3)),
    ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<HostelAllocation?> AllocateBed(Guid tenant,HostelAllocationCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Student(db,tx,tenant,r.CampusId,r.StudentId,ct)||!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM hostel_beds WHERE tenant_id=@t AND campus_id=@c AND id=@bed)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@bed",Id(r.BedId))))return null;
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM hostel_allocations WHERE tenant_id=@t AND campus_id=@c
    AND status='ACTIVE' AND (bed_id=@bed OR student_id=@student))
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@bed",Id(r.BedId)),("@student",Id(r.StudentId))))
   throw new OperationsConflictException("Student already has a bed or bed is occupied.");
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO hostel_allocations(id,tenant_id,campus_id,student_id,bed_id)
   VALUES(@id,@t,@c,@student,@bed)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
     ("@student",Id(r.StudentId)),("@bed",Id(r.BedId)));
  await tx.CommitAsync(ct);return new HostelAllocation(id,r.StudentId,r.BedId,"ACTIVE");
 }
 public async Task<bool> Vacate(Guid tenant,Guid campus,Guid allocation,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  var changed=await Exec(db,tx,"""
   UPDATE hostel_allocations SET status='VACATED',vacated_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='ACTIVE'
   """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(allocation)));
  await tx.CommitAsync(ct);return changed==1;
 }
 public async Task<IReadOnlyList<HostelAllocation>> Allocations(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,student_id,bed_id,status FROM hostel_allocations
   WHERE tenant_id=@t AND campus_id=@c ORDER BY assigned_utc DESC LIMIT 500
   """,r=>new HostelAllocation(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3)),
    ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<OperationsDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
    (SELECT COUNT(*) FROM library_titles WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM library_copies WHERE tenant_id=@t AND campus_id=@c AND status='AVAILABLE'),
    (SELECT COUNT(*) FROM library_loans WHERE tenant_id=@t AND campus_id=@c AND status='OPEN'),
    (SELECT COUNT(*) FROM library_loans WHERE tenant_id=@t AND campus_id=@c AND status='OPEN' AND due_on<UTC_DATE()),
    (SELECT COALESCE(SUM(fine_amount),0) FROM library_loans WHERE tenant_id=@t AND campus_id=@c AND status='RETURNED'),
    (SELECT COUNT(*) FROM transport_routes WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM transport_assignments WHERE tenant_id=@t AND campus_id=@c AND status='ACTIVE'),
    (SELECT COUNT(*) FROM hostel_beds WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM hostel_allocations WHERE tenant_id=@t AND campus_id=@c AND status='ACTIVE')
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
  return new OperationsDashboard(Convert.ToInt32(r.GetInt64(0)),Convert.ToInt32(r.GetInt64(1)),
    Convert.ToInt32(r.GetInt64(2)),Convert.ToInt32(r.GetInt64(3)),r.GetDecimal(4),
    Convert.ToInt32(r.GetInt64(5)),Convert.ToInt32(r.GetInt64(6)),
    Convert.ToInt32(r.GetInt64(7)),Convert.ToInt32(r.GetInt64(8)));
 }
}
