using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Finance;

public interface IFinanceStore
{
 Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct);
 Task<FeeStructureRecord?> AddFeeAsync(Guid tenant,FeeStructureCreate request,CancellationToken ct);
 Task<IReadOnlyList<FeeStructureRecord>> FeesAsync(Guid tenant,Guid campus,CancellationToken ct);
 Task<ChargeRecord?> AssignAsync(Guid tenant,ChargeCreate request,CancellationToken ct);
 Task<IReadOnlyList<ChargeRecord>> ChargesAsync(Guid tenant,Guid campus,CancellationToken ct);
 Task<IReadOnlyList<InstallmentRecord>> InstallmentsAsync(Guid tenant,Guid campus,Guid charge,CancellationToken ct);
 Task<ConcessionRecord?> ConcedeAsync(Guid tenant,Guid actor,ConcessionCreate request,CancellationToken ct);
 Task<PaymentRecord?> CollectAsync(Guid tenant,Guid actor,PaymentCreate request,string key,CancellationToken ct);
 Task<IReadOnlyList<PaymentRecord>> PaymentsAsync(Guid tenant,Guid campus,CancellationToken ct);
 Task<ReceiptRecord?> ReceiptAsync(Guid tenant,Guid campus,Guid payment,CancellationToken ct);
 Task<RefundRecord?> RefundAsync(Guid tenant,Guid actor,RefundCreate request,string key,CancellationToken ct);
 Task<bool> ReconcileAsync(Guid tenant,Guid actor,ReconcileRequest request,CancellationToken ct);
 Task<FinanceDashboard> DashboardAsync(Guid tenant,Guid campus,CancellationToken ct);
}
public sealed class FinanceScopeException:Exception { }
public sealed class FinanceConflictException(string message):Exception(message) { }
