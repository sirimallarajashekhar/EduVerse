using EduVerse.Contracts;
using MySqlConnector;
namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlHrmsStore
{
 public async Task<HrSalary?> Salary(Guid tenant,HrSalaryCreate input,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Employee(db,tx,tenant,input.CampusId,input.EmployeeId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO hr_salary_structures(id,tenant_id,campus_id,employee_id,
    effective_on,base_amount,allowance_amount,deduction_amount)
   VALUES(@id,@t,@c,@employee,@date,@base,@allowance,@deduction)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(input.CampusId)),
   ("@employee",Id(input.EmployeeId)),("@date",Day(input.EffectiveOn)),
   ("@base",input.BaseAmount),("@allowance",input.Allowances),("@deduction",input.Deductions));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new HrSalary(id,input.EmployeeId,input.EffectiveOn,
   input.BaseAmount,input.Allowances,input.Deductions);
 }
 public async Task<HrPayrollPreview?> Preview(Guid tenant,string actor,Guid campus,Guid employee,
   DateOnly month,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var tx=await db.BeginTransactionAsync(ct);
  await LockTenant(db,tx,tenant,ct);
  if(!await Employee(db,tx,tenant,campus,employee,ct))return null;
  decimal baseAmount,allowance,deductions;
  await using(var cmd=new MySqlCommand("""
   SELECT base_amount,allowance_amount,deduction_amount FROM hr_salary_structures
   WHERE tenant_id=@t AND campus_id=@c AND employee_id=@employee AND effective_on<=@month
   ORDER BY effective_on DESC LIMIT 1
   """,db,tx))
  {
   Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@employee",Id(employee)),
    ("@month",Day(month)));
   await using var r=await cmd.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))return null;
   baseAmount=r.GetDecimal(0);allowance=r.GetDecimal(1);deductions=r.GetDecimal(2);
  }
  var gross=baseAmount+allowance;var net=gross-deductions;
  await using(var cmd=new MySqlCommand("""
   INSERT INTO hr_payroll_previews(id,tenant_id,campus_id,employee_id,payroll_month,
    gross_amount,deduction_amount,net_amount,calculated_by)
   VALUES(@id,@t,@c,@employee,@month,@gross,@deductions,@net,@actor)
   ON DUPLICATE KEY UPDATE gross_amount=VALUES(gross_amount),
     deduction_amount=VALUES(deduction_amount),net_amount=VALUES(net_amount),
     calculated_by=VALUES(calculated_by),created_utc=UTC_TIMESTAMP(6)
   """,db,tx))
  {
   Add(cmd,("@id",Id(Guid.NewGuid())),("@t",Id(tenant)),("@c",Id(campus)),
    ("@employee",Id(employee)),("@month",Day(month)),("@gross",gross),
    ("@deductions",deductions),("@net",net),("@actor",actor));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new HrPayrollPreview(employee,month,gross,deductions,net);
 }
 public async Task<IReadOnlyList<HrStaffAttendance>> Attendance(Guid tenant,Guid campus,DateOnly date,CancellationToken ct)
 {
  var records=new List<HrStaffAttendance>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT e.id,a.attendance_date,a.status FROM hr_employees e
   JOIN staff_attendance a ON a.tenant_id=e.tenant_id AND a.campus_id=e.campus_id
     AND a.staff_subject_id=e.user_subject_id
   WHERE e.tenant_id=@t AND e.campus_id=@c AND a.attendance_date=@date
   ORDER BY e.employee_number LIMIT 500
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)),("@date",Day(date)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))records.Add(new HrStaffAttendance(
   r.GetGuid(0),DateOnly.FromDateTime(r.GetDateTime(1)),r.GetString(2)));
  return records;
 }
 public async Task<HrDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
    (SELECT COUNT(*) FROM hr_employees WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM hr_employees WHERE tenant_id=@t AND campus_id=@c AND status='ACTIVE'),
    (SELECT COUNT(*) FROM hr_leave_requests WHERE tenant_id=@t AND campus_id=@c AND status='PENDING'),
    (SELECT COUNT(*) FROM hr_leave_requests WHERE tenant_id=@t AND campus_id=@c AND status='APPROVED'),
    (SELECT COALESCE(SUM(net_amount),0) FROM hr_payroll_previews WHERE tenant_id=@t AND campus_id=@c
       AND payroll_month=DATE_FORMAT(UTC_DATE(),'%Y-%m-01'))
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
  return new HrDashboard(Convert.ToInt32(r.GetInt64(0)),Convert.ToInt32(r.GetInt64(1)),
   Convert.ToInt32(r.GetInt64(2)),Convert.ToInt32(r.GetInt64(3)),r.GetDecimal(4));
 }
}
