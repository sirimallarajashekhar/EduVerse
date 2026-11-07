using EduVerse.Application.Hrms;
using EduVerse.Contracts;
using MySqlConnector;
namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlHrmsStore
{
 public async Task<HrLeaveType?> LeaveType(Guid tenant,HrLeaveTypeCreate input,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,input.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO hr_leave_types(id,tenant_id,campus_id,code,title,annual_days)
   VALUES(@id,@t,@c,@code,@title,@days)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(input.CampusId)),
   ("@code",input.Code),("@title",input.Title),("@days",input.AnnualDays));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new HrLeaveType(id,input.CampusId,input.Code,input.Title,input.AnnualDays);
 }
 public async Task<IReadOnlyList<HrLeaveType>> LeaveTypes(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<HrLeaveType>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,code,title,annual_days FROM hr_leave_types
   WHERE tenant_id=@t AND campus_id=@c ORDER BY code LIMIT 100
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new HrLeaveType(r.GetGuid(0),r.GetGuid(1),
   r.GetString(2),r.GetString(3),r.GetDecimal(4)));
  return list;
 }
 public async Task<HrLeave?> RequestLeave(Guid tenant,HrLeaveCreate input,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Employee(db,tx,tenant,input.CampusId,input.EmployeeId,ct))return null;
  if(!await Check(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM hr_leave_types WHERE id=@type AND tenant_id=@t AND campus_id=@c)
   """,ct,("@type",Id(input.LeaveTypeId)),("@t",Id(tenant)),("@c",Id(input.CampusId))))return null;
  if(await Check(db,tx,"""
    SELECT EXISTS(SELECT 1 FROM hr_leave_requests WHERE tenant_id=@t AND campus_id=@c
     AND employee_id=@employee AND status IN ('PENDING','APPROVED')
     AND from_date<=@end AND to_date>=@start)
    """,ct,("@t",Id(tenant)),("@c",Id(input.CampusId)),
    ("@employee",Id(input.EmployeeId)),("@start",Day(input.From)),("@end",Day(input.To))))
   throw new HrConflictException("Employee has a pending or approved overlapping leave.");
  var id=Guid.NewGuid();
  var days=input.To.DayNumber-input.From.DayNumber+1;
  await using var cmd=new MySqlCommand("""
   INSERT INTO hr_leave_requests(id,tenant_id,campus_id,employee_id,leave_type_id,
    from_date,to_date,days,reason)
   VALUES(@id,@t,@c,@employee,@type,@start,@end,@days,@reason)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(input.CampusId)),
   ("@employee",Id(input.EmployeeId)),("@type",Id(input.LeaveTypeId)),
   ("@start",Day(input.From)),("@end",Day(input.To)),("@days",days),("@reason",input.Reason));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new HrLeave(id,input.EmployeeId,input.LeaveTypeId,input.From,input.To,days,"PENDING");
 }
 public async Task<IReadOnlyList<HrLeave>> Leaves(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<HrLeave>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,employee_id,leave_type_id,from_date,to_date,days,status
   FROM hr_leave_requests WHERE tenant_id=@t AND campus_id=@c
   ORDER BY created_utc DESC LIMIT 250
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))list.Add(new HrLeave(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),
   DateOnly.FromDateTime(r.GetDateTime(3)),DateOnly.FromDateTime(r.GetDateTime(4)),
   r.GetDecimal(5),r.GetString(6)));
  return list;
 }
 public async Task<bool> DecideLeave(Guid tenant,string actor,Guid campus,Guid leave,string decision,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  Guid employee,type;DateOnly from;decimal days;
  await using(var find=new MySqlCommand("""
   SELECT employee_id,leave_type_id,from_date,days FROM hr_leave_requests
   WHERE tenant_id=@t AND campus_id=@c AND id=@leave AND status='PENDING' FOR UPDATE
   """,db,tx))
  {
   Add(find,("@t",Id(tenant)),("@c",Id(campus)),("@leave",Id(leave)));
   await using var r=await find.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))return false;
   employee=r.GetGuid(0);type=r.GetGuid(1);
   from=DateOnly.FromDateTime(r.GetDateTime(2));days=r.GetDecimal(3);
  }
  if(decision=="APPROVED")
  {
   if(!await Employee(db,tx,tenant,campus,employee,ct))
    throw new HrConflictException("Cannot approve leave for exited employee.");
   // Create calendar-year entitlement only once. The tenant-wide lock serializes competing approvals.
   await using(var seed=new MySqlCommand("""
    INSERT INTO hr_leave_balances(tenant_id,campus_id,employee_id,leave_type_id,year,allowance)
    SELECT @t,@c,@employee,t.id,@year,t.annual_days
    FROM hr_leave_types t WHERE t.id=@type AND t.tenant_id=@t AND t.campus_id=@c
    ON DUPLICATE KEY UPDATE allowance=allowance
    """,db,tx))
   {
    Add(seed,("@t",Id(tenant)),("@c",Id(campus)),("@employee",Id(employee)),
     ("@year",from.Year),("@type",Id(type)));
    await seed.ExecuteNonQueryAsync(ct);
   }
   await using(var deduct=new MySqlCommand("""
    UPDATE hr_leave_balances SET used=used+@days
    WHERE tenant_id=@t AND campus_id=@c AND employee_id=@employee
     AND leave_type_id=@type AND year=@year AND used+@days<=allowance
    """,db,tx))
   {
    Add(deduct,("@t",Id(tenant)),("@c",Id(campus)),("@employee",Id(employee)),
     ("@type",Id(type)),("@year",from.Year),("@days",days));
    if(await deduct.ExecuteNonQueryAsync(ct)!=1)
     throw new HrConflictException("Leave balance is insufficient.");
   }
  }
  await using(var update=new MySqlCommand("""
   UPDATE hr_leave_requests SET status=@decision,reviewed_by=@actor,reviewed_utc=UTC_TIMESTAMP(6)
   WHERE tenant_id=@t AND campus_id=@c AND id=@leave AND status='PENDING'
   """,db,tx))
  {
   Add(update,("@decision",decision),("@actor",actor),("@t",Id(tenant)),
    ("@c",Id(campus)),("@leave",Id(leave)));
   if(await update.ExecuteNonQueryAsync(ct)!=1)throw new HrConflictException("Leave was already reviewed.");
  }
  await tx.CommitAsync(ct);return true;
 }
 public async Task<HrLeaveBalance?> Balance(Guid tenant,Guid campus,Guid employee,Guid type,int year,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT t.annual_days,COALESCE(b.used,0) FROM hr_leave_types t
   JOIN hr_employees e ON e.tenant_id=t.tenant_id AND e.campus_id=t.campus_id
    AND e.id=@employee
   LEFT JOIN hr_leave_balances b ON b.tenant_id=t.tenant_id AND b.campus_id=t.campus_id
     AND b.employee_id=e.id AND b.leave_type_id=t.id AND b.year=@year
   WHERE t.tenant_id=@t AND t.campus_id=@c AND t.id=@type
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),
   ("@employee",Id(employee)),("@type",Id(type)),("@year",year));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  if(!await r.ReadAsync(ct))return null;
  var allowed=r.GetDecimal(0);var used=r.GetDecimal(1);
  return new HrLeaveBalance(employee,type,year,allowed,used,allowed-used);
 }
}
