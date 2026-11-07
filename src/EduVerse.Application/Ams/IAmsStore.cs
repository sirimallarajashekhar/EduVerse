using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.Ams;
public interface IAmsStore
{
 Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct);
 Task<AmsCategory?> AddCategory(Guid tenant,AmsCategoryCreate request,CancellationToken ct);
 Task<IReadOnlyList<AmsCategory>> Categories(Guid tenant,Guid campus,CancellationToken ct);
 Task<AmsVendor?> AddVendor(Guid tenant,AmsVendorCreate request,CancellationToken ct);
 Task<IReadOnlyList<AmsVendor>> Vendors(Guid tenant,Guid campus,CancellationToken ct);
 Task<AmsAsset?> Register(Guid tenant,string actor,AmsAssetCreate request,CancellationToken ct);
 Task<IReadOnlyList<AmsAsset>> Assets(Guid tenant,Guid campus,CancellationToken ct);
 Task<AmsAsset?> Allocate(Guid tenant,string actor,AmsAllocation request,CancellationToken ct);
 Task<AmsAsset?> Lifecycle(Guid tenant,string actor,AmsAssetAction request,CancellationToken ct);
 Task<IReadOnlyList<AmsAssetEvent>> AssetEvents(Guid tenant,Guid campus,Guid asset,CancellationToken ct);
 Task<AmsWorkOrder?> CreateWorkOrder(Guid tenant,AmsMaintenanceCreate request,CancellationToken ct);
 Task<IReadOnlyList<AmsWorkOrder>> WorkOrders(Guid tenant,Guid campus,CancellationToken ct);
 Task<bool> CompleteWorkOrder(Guid tenant,string actor,Guid campus,Guid workOrder,CancellationToken ct);
 Task<AmsStock?> AddStock(Guid tenant,AmsStockCreate request,CancellationToken ct);
 Task<IReadOnlyList<AmsStock>> Stock(Guid tenant,Guid campus,CancellationToken ct);
 Task<AmsMovement?> MoveStock(Guid tenant,string actor,AmsStockMove request,string key,CancellationToken ct);
 Task<AmsPurchase?> AddPurchase(Guid tenant,AmsPurchaseCreate request,CancellationToken ct);
 Task<IReadOnlyList<AmsPurchase>> Purchases(Guid tenant,Guid campus,CancellationToken ct);
 Task<AmsDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct);
}
public sealed class AmsScopeException:Exception {}
public sealed class AmsConflictException(string message):Exception(message) {}
