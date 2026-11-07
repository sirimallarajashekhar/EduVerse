using System.Security.Cryptography;
using System.Text;
using EduVerse.Application.Finance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlFinanceStore
{
 private static byte[] Digest(params string?[] parts) =>
  SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",parts.Select(x=>x??""))));

 public async Task<ConcessionRecord?> ConcedeAsync(Guid tenant,Guid actor,ConcessionCreate req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await LockCharge(db,tx,tenant,req.CampusId,req.ChargeId,ct))return null;
  var (gross,concession,paid,refunded)=await Totals(db,tx,tenant,req.CampusId,req.ChargeId,ct);
  if(req.Amount>gross-concession-paid+refunded)
   throw new FinanceConflictException("Concession exceeds currently outstanding dues.");
  var id=Guid.NewGuid();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO finance_concessions(id,tenant_id,campus_id,charge_id,category,reason,amount,approved_by)
   VALUES(@id,@tenant,@campus,@charge,@category,@reason,@amount,@actor)
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@charge",Id(req.ChargeId)),("@category",req.Category),("@reason",req.Reason),
    ("@amount",req.Amount),("@actor",Id(actor)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await using(var update=new MySqlCommand("""
   UPDATE finance_student_charges SET concession_amount=concession_amount+@amount
   WHERE id=@charge AND tenant_id=@tenant AND campus_id=@campus
   """,db,tx))
  {
   Add(update,("@amount",req.Amount),("@charge",Id(req.ChargeId)),
    ("@tenant",Id(tenant)),("@campus",Id(req.CampusId)));
   await update.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new ConcessionRecord(id,req.ChargeId,req.Category,req.Reason,req.Amount);
 }

 public async Task<PaymentRecord?> CollectAsync(Guid tenant,Guid actor,PaymentCreate req,string key,CancellationToken ct)
 {
  var digest=Digest(Id(req.CampusId),Id(req.InstallmentId),req.Amount.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
   req.Method,req.Reference);
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  // Per-tenant serialization closes concurrent write gaps and ensures key replay consistency.
  await using(var tenantLock=new MySqlCommand("""
   SELECT id FROM tenants WHERE id=@tenant AND is_active=1 FOR UPDATE
   """,db,tx))
  {
   Add(tenantLock,("@tenant",Id(tenant)));
   if(await tenantLock.ExecuteScalarAsync(ct) is null)return null;
  }
  await using(var replay=new MySqlCommand("""
   SELECT id,campus_id,charge_id,installment_id,amount,method,receipt_number,
     reconciliation_status,request_sha256 FROM finance_payments
   WHERE tenant_id=@tenant AND idempotency_key=@key
   """,db,tx))
  {
   Add(replay,("@tenant",Id(tenant)),("@key",key));
   await using var reader=await replay.ExecuteReaderAsync(ct);
   if(await reader.ReadAsync(ct))
   {
    var saved=(byte[])reader.GetValue(8);
    if(!CryptographicOperations.FixedTimeEquals(saved,digest))
     throw new FinanceConflictException("Idempotency key was used with different payment details.");
    var previous=new PaymentRecord(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),
     reader.GetGuid(3),reader.GetDecimal(4),reader.GetString(5),reader.GetString(6),reader.GetString(7));
    await reader.DisposeAsync();
    await tx.CommitAsync(ct);
    return previous;
   }
  }
  Guid charge;
  decimal installmentAmount;
  await using(var lookup=new MySqlCommand("""
   SELECT i.charge_id,i.amount FROM finance_installments i
   JOIN finance_student_charges c ON c.id=i.charge_id AND c.tenant_id=i.tenant_id
      AND c.campus_id=i.campus_id
   JOIN students s ON s.id=c.student_id AND s.tenant_id=c.tenant_id AND s.campus_id=c.campus_id
   WHERE i.id=@installment AND i.tenant_id=@tenant AND i.campus_id=@campus
    AND s.status='ACTIVE'
   """,db,tx))
  {
   Add(lookup,("@installment",Id(req.InstallmentId)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)));
   await using var reader=await lookup.ExecuteReaderAsync(ct);
   if(!await reader.ReadAsync(ct))return null;
   charge=reader.GetGuid(0);installmentAmount=reader.GetDecimal(1);
  }
  if(!await LockCharge(db,tx,tenant,req.CampusId,charge,ct))return null;
  var (gross,concession,paid,refunded)=await Totals(db,tx,tenant,req.CampusId,charge,ct);
  if(req.Amount>gross-concession-paid+refunded)
   throw new FinanceConflictException("Payment exceeds outstanding charge.");
  decimal installmentPaid,installmentRefunded;
  await using(var totals=new MySqlCommand("""
   SELECT COALESCE(SUM(p.amount),0),
    COALESCE((SELECT SUM(r.amount) FROM finance_refunds r
      JOIN finance_payments rp ON rp.id=r.payment_id
      WHERE rp.tenant_id=@tenant AND rp.campus_id=@campus AND rp.installment_id=@installment),0)
   FROM finance_payments p WHERE p.tenant_id=@tenant AND p.campus_id=@campus AND p.installment_id=@installment
   """,db,tx))
  {
   Add(totals,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),("@installment",Id(req.InstallmentId)));
   await using var reader=await totals.ExecuteReaderAsync(ct);
   await reader.ReadAsync(ct);
   installmentPaid=reader.GetDecimal(0);installmentRefunded=reader.GetDecimal(1);
  }
  if(req.Amount>installmentAmount-installmentPaid+installmentRefunded)
   throw new FinanceConflictException("Payment exceeds outstanding installment.");
  var id=Guid.NewGuid();
  var receipt="EVR-"+id.ToString("N").ToUpperInvariant();
  await using(var cmd=new MySqlCommand("""
   INSERT INTO finance_payments(id,tenant_id,campus_id,charge_id,installment_id,amount,method,
    external_reference,idempotency_key,request_sha256,receipt_number,collected_by)
   VALUES(@id,@tenant,@campus,@charge,@installment,@amount,@method,@reference,@key,@hash,@receipt,@actor)
   """,db,tx))
  {
   Add(cmd,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@charge",Id(charge)),("@installment",Id(req.InstallmentId)),("@amount",req.Amount),
    ("@method",req.Method),("@reference",req.Reference),("@key",key),("@hash",digest),
    ("@receipt",receipt),("@actor",Id(actor)));
   await cmd.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new PaymentRecord(id,req.CampusId,charge,req.InstallmentId,req.Amount,req.Method,receipt,"PENDING");
 }

 public async Task<IReadOnlyList<PaymentRecord>> PaymentsAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<PaymentRecord>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,charge_id,installment_id,amount,method,receipt_number,reconciliation_status
   FROM finance_payments WHERE tenant_id=@tenant AND campus_id=@campus
   ORDER BY collected_utc DESC LIMIT 250
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
   list.Add(new PaymentRecord(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.GetGuid(3),
    reader.GetDecimal(4),reader.GetString(5),reader.GetString(6),reader.GetString(7)));
  return list;
 }

 public async Task<ReceiptRecord?> ReceiptAsync(Guid tenant,Guid campus,Guid payment,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT p.receipt_number,t.display_name,CONCAT(s.given_name,' ',s.family_name),
     s.admission_number,f.title,p.amount,f.currency,p.collected_utc,p.method,p.external_reference
   FROM finance_payments p
   JOIN finance_student_charges c ON c.id=p.charge_id AND c.tenant_id=p.tenant_id AND c.campus_id=p.campus_id
   JOIN students s ON s.id=c.student_id AND s.tenant_id=c.tenant_id AND s.campus_id=c.campus_id
   JOIN finance_fee_structures f ON f.id=c.fee_structure_id AND f.tenant_id=c.tenant_id AND f.campus_id=c.campus_id
   JOIN tenants t ON t.id=p.tenant_id
   WHERE p.tenant_id=@tenant AND p.campus_id=@campus AND p.id=@payment
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)),("@payment",Id(payment)));
  await using var r=await cmd.ExecuteReaderAsync(ct);
  if(!await r.ReadAsync(ct))return null;
  return new ReceiptRecord(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),
   r.GetString(4),r.GetDecimal(5),r.GetString(6),DateTime.SpecifyKind(r.GetDateTime(7),DateTimeKind.Utc),
   r.GetString(8),r.IsDBNull(9)?null:r.GetString(9));
 }
}
