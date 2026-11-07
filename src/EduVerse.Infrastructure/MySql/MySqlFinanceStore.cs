using EduVerse.Application.Finance;
using EduVerse.Contracts;
using EduVerse.Domain;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlFinanceStore(string connectionString):IFinanceStore
{
 private MySqlConnection Connect()=>new(connectionString);
 private static string Id(Guid id)=>id.ToString("D");
 private static void Add(MySqlCommand cmd,params (string Name,object? Value)[] args)
 {
  foreach(var (name,value) in args)cmd.Parameters.AddWithValue(name,value??DBNull.Value);
 }
 private static async Task<bool> Campus(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM campuses WHERE id=@campus AND tenant_id=@tenant AND is_active=1)
   """,db,tx);
  Add(cmd,("@campus",Id(campus)),("@tenant",Id(tenant)));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 private static async Task<bool> LockCharge(MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid charge,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT id FROM finance_student_charges
   WHERE id=@charge AND tenant_id=@tenant AND campus_id=@campus FOR UPDATE
   """,db,tx);
  Add(cmd,("@charge",Id(charge)),("@tenant",Id(tenant)),("@campus",Id(campus)));
  return await cmd.ExecuteScalarAsync(ct) is not null;
 }
 private static async Task<(decimal Gross,decimal Concessions,decimal Paid,decimal Refunded)> Totals(
   MySqlConnection db,MySqlTransaction tx,Guid tenant,Guid campus,Guid charge,CancellationToken ct)
 {
  await using var cmd=new MySqlCommand("""
   SELECT c.gross_amount,c.concession_amount,
    COALESCE((SELECT SUM(p.amount) FROM finance_payments p
      WHERE p.tenant_id=c.tenant_id AND p.campus_id=c.campus_id AND p.charge_id=c.id),0),
    COALESCE((SELECT SUM(r.amount) FROM finance_refunds r
      JOIN finance_payments p ON p.id=r.payment_id AND p.tenant_id=r.tenant_id AND p.campus_id=r.campus_id
      WHERE p.tenant_id=c.tenant_id AND p.campus_id=c.campus_id AND p.charge_id=c.id),0)
   FROM finance_student_charges c
   WHERE c.tenant_id=@tenant AND c.campus_id=@campus AND c.id=@charge
   """,db,tx);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@charge",Id(charge)));
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  if(!await reader.ReadAsync(ct))throw new FinanceScopeException();
  return (reader.GetDecimal(0),reader.GetDecimal(1),reader.GetDecimal(2),reader.GetDecimal(3));
 }
 public async Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM tenant_memberships m
     JOIN tenants t ON t.id=m.tenant_id AND t.is_active=1
     JOIN identity_users u ON u.id=@user AND u.is_active=1
     JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.status='ACTIVE'
       AND (s.expires_utc IS NULL OR s.expires_utc>UTC_TIMESTAMP(6))
     JOIN subscription_plan_features f ON f.plan_code=s.plan_code AND f.feature_code='core'
     JOIN subscription_plans p ON p.plan_code=s.plan_code AND p.is_active=1
     WHERE m.tenant_id=@tenant AND m.subject_id=@user AND m.is_active=1 AND m.role_code='ADMIN')
   """,db);
  Add(cmd,("@tenant",Id(actor.TenantId)),("@user",actor.SubjectId));
  return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
 }
 public async Task<FeeStructureRecord?> AddFeeAsync(Guid tenant,FeeStructureCreate req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,req.CampusId,ct))return null;
  await using(var check=new MySqlCommand("""
   SELECT EXISTS(SELECT 1 FROM academic_years
    WHERE id=@year AND tenant_id=@tenant AND campus_id=@campus AND is_active=1)
   """,db,tx))
  {
   Add(check,("@year",Id(req.AcademicYearId)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)));
   if(Convert.ToInt32(await check.ExecuteScalarAsync(ct))!=1)return null;
  }
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO finance_fee_structures(id,tenant_id,campus_id,academic_year_id,code,title,amount)
   VALUES(@id,@tenant,@campus,@year,@code,@title,@amount)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
   ("@year",Id(req.AcademicYearId)),("@code",req.Code),("@title",req.Title),("@amount",req.Amount));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new FeeStructureRecord(id,req.CampusId,req.AcademicYearId,req.Code,req.Title,req.Amount,"INR");
 }
 public async Task<IReadOnlyList<FeeStructureRecord>> FeesAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  var items=new List<FeeStructureRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,academic_year_id,code,title,amount,currency
   FROM finance_fee_structures WHERE tenant_id=@tenant AND campus_id=@campus
   ORDER BY code LIMIT 200
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))items.Add(new FeeStructureRecord(
   r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetString(3),r.GetString(4),r.GetDecimal(5),r.GetString(6)));
  return items;
 }
 public async Task<ChargeRecord?> AssignAsync(Guid tenant,ChargeCreate req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await using(var lockCampus=new MySqlCommand("""
   SELECT id FROM campuses WHERE tenant_id=@tenant AND id=@campus AND is_active=1 FOR UPDATE
   """,db,tx))
  {
   Add(lockCampus,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)));
   if(await lockCampus.ExecuteScalarAsync(ct) is null)return null;
  }
  Guid student;
  Guid enrollmentYear;
  await using(var studentCmd=new MySqlCommand("""
   SELECT e.student_id,e.academic_year_id FROM student_enrollments e
   JOIN students s ON s.id=e.student_id AND s.tenant_id=e.tenant_id AND s.campus_id=e.campus_id
   WHERE e.id=@enrollment AND e.tenant_id=@tenant AND e.campus_id=@campus
     AND e.status='ACTIVE' AND s.status='ACTIVE'
   """,db,tx))
  {
   Add(studentCmd,("@enrollment",Id(req.EnrollmentId)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)));
   await using var r=await studentCmd.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))return null;
   student=r.GetGuid(0);enrollmentYear=r.GetGuid(1);
  }
  decimal amount;
  await using(var fee=new MySqlCommand("""
   SELECT amount FROM finance_fee_structures WHERE id=@id AND tenant_id=@tenant
    AND campus_id=@campus AND academic_year_id=@year AND is_active=1
   """,db,tx))
  {
   Add(fee,("@id",Id(req.FeeStructureId)),("@tenant",Id(tenant)),
    ("@campus",Id(req.CampusId)),("@year",Id(enrollmentYear)));
   var result=await fee.ExecuteScalarAsync(ct);
   if(result is null)return null;
   amount=Convert.ToDecimal(result);
  }
  if(req.Installments.Sum(x=>x.Amount)!=amount)
   throw new FinanceConflictException("Installments must sum exactly to the assigned fee.");
  var id=Guid.NewGuid();
  await using(var insert=new MySqlCommand("""
   INSERT INTO finance_student_charges
    (id,tenant_id,campus_id,student_id,enrollment_id,fee_structure_id,gross_amount)
   VALUES(@id,@tenant,@campus,@student,@enrollment,@fee,@amount)
   """,db,tx))
  {
   Add(insert,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@student",Id(student)),("@enrollment",Id(req.EnrollmentId)),
    ("@fee",Id(req.FeeStructureId)),("@amount",amount));
   await insert.ExecuteNonQueryAsync(ct);
  }
  foreach(var i in req.Installments)
  {
   await using var cmd=new MySqlCommand("""
    INSERT INTO finance_installments(id,tenant_id,campus_id,charge_id,installment_number,due_on,amount)
    VALUES(@id,@tenant,@campus,@charge,@number,@due,@amount)
    """,db,tx);
   Add(cmd,("@id",Id(Guid.NewGuid())),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@charge",Id(id)),("@number",i.Number),("@due",i.DueOn.ToDateTime(TimeOnly.MinValue)),("@amount",i.Amount));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new ChargeRecord(id,req.CampusId,student,req.EnrollmentId,req.FeeStructureId,amount,0,0,0,amount);
 }
 public async Task<IReadOnlyList<ChargeRecord>> ChargesAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<ChargeRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT c.id,c.campus_id,c.student_id,c.enrollment_id,c.fee_structure_id,
     c.gross_amount,c.concession_amount,
     COALESCE((SELECT SUM(p.amount) FROM finance_payments p WHERE p.charge_id=c.id),0),
     COALESCE((SELECT SUM(r.amount) FROM finance_refunds r JOIN finance_payments p ON p.id=r.payment_id WHERE p.charge_id=c.id),0)
   FROM finance_student_charges c WHERE c.tenant_id=@tenant AND c.campus_id=@campus
   ORDER BY c.created_utc DESC LIMIT 250
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
  {
   var gross=r.GetDecimal(5);var concession=r.GetDecimal(6);
   var paid=r.GetDecimal(7);var refunded=r.GetDecimal(8);
   list.Add(new ChargeRecord(r.GetGuid(0),r.GetGuid(1),r.GetGuid(2),r.GetGuid(3),
    r.GetGuid(4),gross,concession,paid,refunded,gross-concession-paid+refunded));
  }
  return list;
 }
 public async Task<IReadOnlyList<InstallmentRecord>> InstallmentsAsync(Guid tenant,Guid campus,Guid charge,CancellationToken ct)
 {
  var list=new List<InstallmentRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT i.id,i.charge_id,i.installment_number,i.due_on,i.amount,
    COALESCE((SELECT SUM(p.amount) FROM finance_payments p WHERE p.installment_id=i.id),0),
    COALESCE((SELECT SUM(r.amount) FROM finance_refunds r
       JOIN finance_payments p ON p.id=r.payment_id WHERE p.installment_id=i.id),0)
   FROM finance_installments i
   WHERE i.tenant_id=@tenant AND i.campus_id=@campus AND i.charge_id=@charge
   ORDER BY i.installment_number LIMIT 24
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@charge",Id(charge)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  while(await r.ReadAsync(ct))
   list.Add(new InstallmentRecord(r.GetGuid(0),r.GetGuid(1),r.GetInt32(2),
    DateOnly.FromDateTime(r.GetDateTime(3)),r.GetDecimal(4),r.GetDecimal(4)-r.GetDecimal(5)+r.GetDecimal(6)));
  return list;
 }
}
