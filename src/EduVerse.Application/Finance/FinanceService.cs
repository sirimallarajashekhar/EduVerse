using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Finance;

/// <summary>All financial validation and access decisions are API-owned. Currency: INR; amounts: decimal(18,2).</summary>
public sealed class FinanceService(IFinanceStore store,ITenantContext context, EduVerse.Application.Identity.TenantReadAuthorizer reader)
{
 private async Task<TenantIdentity> Admin(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!Guid.TryParse(actor.SubjectId,out _)||
    !await store.IsAdminAsync(actor,ct))
   throw new UnauthorizedAccessException("Active finance administrator required.");
  return actor;
 }
 private static Guid Required(Guid value) => value==Guid.Empty ? throw new ArgumentException("Identifier is required.") : value;
 private static decimal Money(decimal amount,bool positive=true)
 {
  if(amount<0||positive&&amount==0||amount>9999999999999999.99m||
     decimal.Round(amount,2)!=amount)
   throw new ArgumentException("Amount must be a positive INR value with at most 2 decimal places.");
  return amount;
 }
 private static string Text(string? value,int max,bool required=true)
 {
  var text=(value??"").Trim();
  if(text.Length>max||required&&text.Length==0)throw new ArgumentException("Invalid required finance text.");
  return text;
 }
 public async Task<FeeStructureRecord> AddFee(FeeStructureCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Required(input.CampusId);Required(input.AcademicYearId);
  var code=Text(input.Code,30).ToUpperInvariant();
  if(!code.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
   throw new ArgumentException("Fee code must be alphanumeric.");
  var normalized=input with {Code=code,Title=Text(input.Title,160),Amount=Money(input.Amount)};
  return await store.AddFeeAsync(actor.TenantId,normalized,ct)??throw new FinanceScopeException();
 }
 public async Task<IReadOnlyList<FeeStructureRecord>> Fees(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("finance.read",campus,ct);return await store.FeesAsync(actor.TenantId,Required(campus),ct);
 }
 public async Task<ChargeRecord> Assign(ChargeCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Required(input.CampusId);Required(input.EnrollmentId);Required(input.FeeStructureId);
  if(input.Installments is null||input.Installments.Count is < 1 or > 24)
   throw new ArgumentException("Provide 1 to 24 installments.");
  var numbers=new HashSet<int>();
  foreach(var installment in input.Installments)
  {
   if(installment.Number is < 1 or > 24||!numbers.Add(installment.Number)||installment.DueOn==default)
    throw new ArgumentException("Invalid installment number or date.");
   Money(installment.Amount);
  }
  return await store.AssignAsync(actor.TenantId,input,ct)??throw new FinanceScopeException();
 }
 public async Task<IReadOnlyList<ChargeRecord>> Charges(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("finance.read",campus,ct);return await store.ChargesAsync(actor.TenantId,Required(campus),ct);
 }
 public async Task<IReadOnlyList<InstallmentRecord>> Installments(Guid campus,Guid charge,CancellationToken ct)
 {
  var actor=await Admin(ct);
  return await store.InstallmentsAsync(actor.TenantId,Required(campus),Required(charge),ct);
 }
 public async Task<ConcessionRecord> Concede(ConcessionCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Required(input.CampusId);Required(input.ChargeId);
  if(input.Category is not ("SCHOLARSHIP" or "CONCESSION" or "DISCOUNT"))
   throw new ArgumentException("Unsupported concession category.");
  var normalized=input with {Reason=Text(input.Reason,240),Amount=Money(input.Amount)};
  return await store.ConcedeAsync(actor.TenantId,Guid.Parse(actor.SubjectId),normalized,ct)
   ??throw new FinanceScopeException();
 }
 public async Task<PaymentRecord> Collect(PaymentCreate input,string key,CancellationToken ct)
 {
  var actor=await Admin(ct);Required(input.CampusId);Required(input.InstallmentId);
  if(input.Method is not ("CASH" or "BANK_TRANSFER" or "CARD_MANUAL" or "UPI_MANUAL"))
   throw new ArgumentException("Unsupported offline payment method.");
  var normalized=input with {Amount=Money(input.Amount),Reference=Text(input.Reference,100,false)};
  return await store.CollectAsync(actor.TenantId,Guid.Parse(actor.SubjectId),normalized,Key(key),ct)
   ??throw new FinanceScopeException();
 }
 public async Task<IReadOnlyList<PaymentRecord>> Payments(Guid campus,CancellationToken ct)
 {
  var actor=await Admin(ct);return await store.PaymentsAsync(actor.TenantId,Required(campus),ct);
 }
 public async Task<ReceiptRecord> Receipt(Guid campus,Guid payment,CancellationToken ct)
 {
  var actor=await Admin(ct);
  return await store.ReceiptAsync(actor.TenantId,Required(campus),Required(payment),ct)
   ??throw new FinanceScopeException();
 }
 public async Task<RefundRecord> Refund(RefundCreate input,string key,CancellationToken ct)
 {
  var actor=await Admin(ct);Required(input.CampusId);Required(input.PaymentId);
  var normalized=input with {Amount=Money(input.Amount),Reason=Text(input.Reason,240),Reference=Text(input.Reference,100)};
  return await store.RefundAsync(actor.TenantId,Guid.Parse(actor.SubjectId),normalized,Key(key),ct)
   ??throw new FinanceScopeException();
 }
 public async Task Reconcile(ReconcileRequest input,CancellationToken ct)
 {
  var actor=await Admin(ct);Required(input.CampusId);Required(input.PaymentId);
  if(input.Status is not ("MATCHED" or "DISPUTED"))
   throw new ArgumentException("Unsupported reconciliation status.");
  if(!await store.ReconcileAsync(actor.TenantId,Guid.Parse(actor.SubjectId),
    input with {Reference=Text(input.Reference,100,false)},ct))
   throw new FinanceScopeException();
 }
 public async Task<FinanceDashboard> Dashboard(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("finance.read",campus,ct);return await store.DashboardAsync(actor.TenantId,Required(campus),ct);
 }
 private static string Key(string? key)
 {
  if(key is null||key.Length is < 8 or > 128||key.Any(c=>c is < '!' or > '~'))
   throw new ArgumentException("Idempotency-Key must contain 8-128 visible ASCII characters.");
  return key;
 }
}
