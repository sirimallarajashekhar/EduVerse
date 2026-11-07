using EduVerse.Application.OperationsModules;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlOperationsStore
{
 public async Task<TransportVehicle?> Vehicle(Guid tenant,TransportVehicleCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO transport_vehicles(id,tenant_id,campus_id,registration_number,capacity,driver_name,driver_phone)
   VALUES(@id,@t,@c,@reg,@capacity,@driver,@phone)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@reg",r.Registration),("@capacity",r.Capacity),("@driver",r.DriverName),("@phone",r.DriverPhone));
  await tx.CommitAsync(ct);return new TransportVehicle(id,r.Registration,r.Capacity,r.DriverName,r.DriverPhone);
 }
 public async Task<IReadOnlyList<TransportVehicle>> Vehicles(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,registration_number,capacity,driver_name,driver_phone
   FROM transport_vehicles WHERE tenant_id=@t AND campus_id=@c ORDER BY registration_number LIMIT 200
   """,r=>new TransportVehicle(r.GetGuid(0),r.GetString(1),r.GetInt32(2),
     r.GetString(3),r.IsDBNull(4)?null:r.GetString(4)),
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<TransportRoute?> Route(Guid tenant,TransportRouteCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM transport_vehicles WHERE id=@vehicle AND tenant_id=@t AND campus_id=@c)
   """,ct,("@vehicle",Id(r.VehicleId)),("@t",Id(tenant)),("@c",Id(r.CampusId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO transport_routes(id,tenant_id,campus_id,vehicle_id,code,name)
   VALUES(@id,@t,@c,@vehicle,@code,@name)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@vehicle",Id(r.VehicleId)),("@code",r.Code),("@name",r.Name));
  await tx.CommitAsync(ct);return new TransportRoute(id,r.VehicleId,r.Code,r.Name);
 }
 public async Task<IReadOnlyList<TransportRoute>> Routes(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,vehicle_id,code,name FROM transport_routes
   WHERE tenant_id=@t AND campus_id=@c ORDER BY code LIMIT 200
   """,r=>new TransportRoute(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetString(3)),
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<TransportStop?> Stop(Guid tenant,TransportStopCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"SELECT EXISTS(SELECT 1 FROM transport_routes WHERE id=@route AND tenant_id=@t AND campus_id=@c)",
    ct,("@route",Id(r.RouteId)),("@t",Id(tenant)),("@c",Id(r.CampusId))))return null;
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO transport_stops(id,tenant_id,campus_id,route_id,name,stop_order)
   VALUES(@id,@t,@c,@route,@name,@order)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@route",Id(r.RouteId)),("@name",r.Name),("@order",r.Order));
  await tx.CommitAsync(ct);return new TransportStop(id,r.RouteId,r.Name,r.Order);
 }
 public async Task<IReadOnlyList<TransportStop>> Stops(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,route_id,name,stop_order FROM transport_stops WHERE tenant_id=@t
    AND campus_id=@c ORDER BY route_id,stop_order LIMIT 500
   """,r=>new TransportStop(r.GetGuid(0),r.GetGuid(1),r.GetString(2),r.GetInt32(3)),
   ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
 public async Task<TransportAssignment?> AssignRoute(Guid tenant,TransportAssignmentCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Student(db,tx,tenant,r.CampusId,r.StudentId,ct))return null;
  var capacity=await Scalar(db,tx,"""
   SELECT v.capacity FROM transport_routes tr
   JOIN transport_vehicles v ON v.id=tr.vehicle_id AND v.tenant_id=tr.tenant_id AND v.campus_id=tr.campus_id
   JOIN transport_stops st ON st.route_id=tr.id AND st.tenant_id=tr.tenant_id
    AND st.campus_id=tr.campus_id AND st.id=@stop
   WHERE tr.tenant_id=@t AND tr.campus_id=@c AND tr.id=@route
   """,ct,("@stop",Id(r.StopId)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@route",Id(r.RouteId)));
  if(capacity is null)return null;
  if(await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM transport_assignments WHERE tenant_id=@t AND campus_id=@c
     AND student_id=@student AND status='ACTIVE')
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@student",Id(r.StudentId))))
   throw new OperationsConflictException("Student already has an active route. Release it before reassignment.");
  var vehicleCount=Convert.ToInt32(await Scalar(db,tx,"""
   SELECT COUNT(*) FROM transport_assignments ta
   JOIN transport_routes tr ON tr.id=ta.route_id AND tr.tenant_id=ta.tenant_id AND tr.campus_id=ta.campus_id
   JOIN transport_routes target ON target.vehicle_id=tr.vehicle_id AND target.tenant_id=tr.tenant_id AND target.campus_id=tr.campus_id
   WHERE target.id=@route AND ta.tenant_id=@t AND ta.campus_id=@c AND ta.status='ACTIVE'
   """,ct,("@route",Id(r.RouteId)),("@t",Id(tenant)),("@c",Id(r.CampusId))));
  if(vehicleCount>=Convert.ToInt32(capacity))
   throw new OperationsConflictException("Vehicle capacity is full.");
  var id=Guid.NewGuid();
  await Exec(db,tx,"""
   INSERT INTO transport_assignments(id,tenant_id,campus_id,student_id,route_id,stop_id)
   VALUES(@id,@t,@c,@student,@route,@stop)
   """,ct,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@student",Id(r.StudentId)),("@route",Id(r.RouteId)),("@stop",Id(r.StopId)));
  await tx.CommitAsync(ct);return new TransportAssignment(id,r.StudentId,r.RouteId,r.StopId,"ACTIVE");
 }
 public async Task<bool> ReleaseRoute(Guid tenant,Guid campus,Guid assignment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  var count=await Exec(db,tx,"""
   UPDATE transport_assignments SET status='RELEASED',released_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@id AND status='ACTIVE'
   """,ct,("@t",Id(tenant)),("@c",Id(campus)),("@id",Id(assignment)));
  await tx.CommitAsync(ct);return count==1;
 }
 public async Task<IReadOnlyList<TransportAssignment>> RouteAssignments(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  return await Query(db,null,"""
   SELECT id,student_id,route_id,stop_id,status FROM transport_assignments
   WHERE tenant_id=@t AND campus_id=@c ORDER BY assigned_utc DESC LIMIT 500
   """,r=>new TransportAssignment(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),r.GetString(4)),
    ct,("@t",Id(tenant)),("@c",Id(campus)));
 }
}
