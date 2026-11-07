using System.Security.Cryptography;
using EduVerse.Application.Finance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;

public sealed partial class MySqlFinanceStore
{
 public async Task<RefundRecord?> RefundAsync(Guid tenant,Guid actor,RefundCreate req,string key,CancellationToken ct)
 {
  var digest=Digest(Id(req.CampusId),Id(req.PaymentId),
   req.Amount.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),req.Reason,req.Reference);
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  await using(var gate=new MySqlCommand("SELECT id FROM tenants WHERE id=@tenant AND is_active=1 FOR UPDATE",db,tx))
  {
   Add(gate,("@tenant",Id(tenant)));
   if(await gate.ExecuteScalarAsync(ct) is null)return null;
  }
  await using(var replay=new MySqlCommand("""
   SELECT id,payment_id,amount,refund_reference,request_sha256 FROM finance_refunds
   WHERE tenant_id=@tenant AND idempotency_key=@key
   """,db,tx))
  {
   Add(replay,("@tenant",Id(tenant)),("@key",key));
   await using var r=await replay.ExecuteReaderAsync(ct);
   if(await r.ReadAsync(ct))
   {
    if(!CryptographicOperations.FixedTimeEquals(digest,(byte[])r.GetValue(4)))
     throw new FinanceConflictException("Idempotency key was used for a different refund.");
    var saved=new RefundRecord(r.GetGuid(0),r.GetGuid(1),r.GetDecimal(2),r.GetString(3));
    await r.DisposeAsync();await tx.CommitAsync(ct);
    return saved;
   }
  }
  Guid charge;
  decimal original;
  await using(var payment=new MySqlCommand("""
   SELECT charge_id,amount FROM finance_payments
   WHERE id=@payment AND tenant_id=@tenant AND campus_id=@campus
   """,db,tx))
  {
   Add(payment,("@payment",Id(req.PaymentId)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)));
   await using var r=await payment.ExecuteReaderAsync(ct);
   if(!await r.ReadAsync(ct))return null;
   charge=r.GetGuid(0);original=r.GetDecimal(1);
  }
  if(!await LockCharge(db,tx,tenant,req.CampusId,charge,ct))return null;
  await using(var totals=new MySqlCommand("""
   SELECT COALESCE(SUM(amount),0) FROM finance_refunds
   WHERE tenant_id=@tenant AND campus_id=@campus AND payment_id=@payment
   """,db,tx))
  {
   Add(totals,("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),("@payment",Id(req.PaymentId)));
   if(req.Amount>original-Convert.ToDecimal(await totals.ExecuteScalarAsync(ct)))
    throw new FinanceConflictException("Refund exceeds the net collected amount.");
  }
  var id=Guid.NewGuid();
  await using(var add=new MySqlCommand("""
   INSERT INTO finance_refunds(id,tenant_id,campus_id,payment_id,amount,reason,
      refund_reference,idempotency_key,request_sha256,refunded_by)
   VALUES(@id,@tenant,@campus,@payment,@amount,@reason,@reference,@key,@hash,@actor)
   """,db,tx))
  {
   Add(add,("@id",Id(id)),("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
    ("@payment",Id(req.PaymentId)),("@amount",req.Amount),("@reason",req.Reason),
    ("@reference",req.Reference),("@key",key),("@hash",digest),("@actor",Id(actor)));
   await add.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new RefundRecord(id,req.PaymentId,req.Amount,req.Reference);
 }

 public async Task<bool> ReconcileAsync(Guid tenant,Guid actor,ReconcileRequest req,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   UPDATE finance_payments SET reconciliation_status=@status,reconciled_utc=UTC_TIMESTAMP(6)
   WHERE id=@payment AND tenant_id=@tenant AND campus_id=@campus AND reconciliation_status='PENDING'
     AND (@reference IS NULL OR external_reference=@reference)
   """,db);
  Add(cmd,("@status",req.Status),("@payment",Id(req.PaymentId)),
   ("@tenant",Id(tenant)),("@campus",Id(req.CampusId)),
   ("@reference",string.IsNullOrEmpty(req.Reference)?null:req.Reference));
  return await cmd.ExecuteNonQueryAsync(ct)==1;
 }

 public async Task<FinanceDashboard> DashboardAsync(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
     (SELECT COUNT(*) FROM finance_fee_structures WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COUNT(*) FROM finance_student_charges WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COUNT(*) FROM finance_payments WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COALESCE(SUM(gross_amount),0) FROM finance_student_charges
       WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COALESCE(SUM(concession_amount),0) FROM finance_student_charges
       WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COALESCE(SUM(amount),0) FROM finance_payments
       WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COALESCE(SUM(amount),0) FROM finance_refunds
       WHERE tenant_id=@tenant AND campus_id=@campus),
     (SELECT COALESCE(SUM(c.gross_amount-c.concession_amount),0)
       FROM finance_student_charges c WHERE c.tenant_id=@tenant AND c.campus_id=@campus),
     (SELECT COALESCE(SUM(i.amount),0) FROM finance_installments i
       WHERE i.tenant_id=@tenant AND i.campus_id=@campus AND i.due_on<UTC_DATE())
   """,db);
  Add(cmd,("@tenant",Id(tenant)),("@campus",Id(campus)));
  await using var r=await cmd.ExecuteReaderAsync(ct);await r.ReadAsync(ct);
  decimal gross=r.GetDecimal(3),concessions=r.GetDecimal(4),paid=r.GetDecimal(5),refunded=r.GetDecimal(6);
  decimal overdueGross=r.GetDecimal(8);
  // Outstanding is net of refunds and concessions; overdue is capped at outstanding,
  // a conservative snapshot until installment-level concession allocation is introduced.
  var outstanding=Math.Max(0,gross-concessions-paid+refunded);
  return new FinanceDashboard(Convert.ToInt32(r.GetInt64(0)),Convert.ToInt32(r.GetInt64(1)),
   Convert.ToInt32(r.GetInt64(2)),gross,concessions,paid,refunded,outstanding,
   Math.Min(outstanding,overdueGross));
 }
}
