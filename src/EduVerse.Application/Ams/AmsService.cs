using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.Ams;
public sealed class AmsService(IAmsStore store,ITenantContext context, EduVerse.Application.Identity.IIdentityStore identities)
{
 private async Task<TenantIdentity> Admin(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!await store.IsAdminAsync(actor,ct))
   throw new UnauthorizedAccessException("Active AMS administrator and subscription required.");
  return actor;
 }
 private static Guid Id(Guid value)=>value==Guid.Empty?throw new ArgumentException("Identifier required."):value;
 private static string Text(string? value,int max)
 {
  var s=(value??"").Trim();
  if(s.Length<1||s.Length>max)throw new ArgumentException("Invalid text value.");
  return s;
 }
 private static string Code(string? value,int max)
 {
  var s=Text(value,max).ToUpperInvariant();
  if(!s.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
   throw new ArgumentException("Codes accept uppercase alphanumerics and hyphens.");
  return s;
 }
 private static decimal Quantity(decimal value,bool allowZero=false)
 {
  if(value<0||!allowZero&&value==0||value>9999999999999999.99m||decimal.Round(value,2)!=value)
   throw new ArgumentException("Quantity must have at most two decimal places.");
  return value;
 }
 public async Task<AmsCategory> AddCategory(AmsCategoryCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);
  return await store.AddCategory(a.TenantId,r with {Code=Code(r.Code,30),Name=Text(r.Name,120)},ct)
   ??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsCategory>> Categories(Guid c,CancellationToken ct)
 {var a=await Admin(ct);return await store.Categories(a.TenantId,Id(c),ct);}
 public async Task<AmsVendor> AddVendor(AmsVendorCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);
  if(r.Email?.Length>254)throw new ArgumentException("Vendor email too long.");
  return await store.AddVendor(a.TenantId,r with {Name=Text(r.Name,160)},ct)??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsVendor>> Vendors(Guid c,CancellationToken ct)
 {var a=await Admin(ct);return await store.Vendors(a.TenantId,Id(c),ct);}
 public async Task<AmsAsset> Register(AmsAssetCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.CategoryId);
  if(r.SerialNumber?.Length>100||r.PurchaseAmount is decimal cost&&Quantity(cost,true)<0)
   throw new ArgumentException("Invalid purchase metadata.");
  return await store.Register(a.TenantId,a.SubjectId,r with {Name=Text(r.Name,160)},ct)
   ??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsAsset>> Assets(Guid c,CancellationToken ct)
 {
  if(!context.TryGet(out var a)||a is null || c==Guid.Empty ||
     !await identities.HasPermissionAsync(a,"ams.read",c,ct))
    throw new UnauthorizedAccessException("Campus-scoped AMS read access required.");
  return await store.Assets(a.TenantId,c,ct);
 }
 public async Task<AmsAsset> Allocate(AmsAllocation r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.AssetId);
  if(r.Note?.Length>300)throw new ArgumentException("Note is too long.");
  return await store.Allocate(a.TenantId,a.SubjectId,r,ct)??throw new AmsScopeException();
 }
 public async Task<AmsAsset> Lifecycle(AmsAssetAction r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.AssetId);
  if(r.Action is not ("RETURN" or "MAINTENANCE" or "REPAIR" or "RETIRE" or "DISPOSE"))
   throw new ArgumentException("Invalid asset lifecycle action.");
  if(r.Note?.Length>300)throw new ArgumentException("Note is too long.");
  return await store.Lifecycle(a.TenantId,a.SubjectId,r,ct)??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsAssetEvent>> Events(Guid c,Guid asset,CancellationToken ct)
 {var a=await Admin(ct);return await store.AssetEvents(a.TenantId,Id(c),Id(asset),ct);}
 public async Task<AmsWorkOrder> CreateWorkOrder(AmsMaintenanceCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.AssetId);
  if(r.DueOn==default)throw new ArgumentException("Due date required.");
  return await store.CreateWorkOrder(a.TenantId,r with {Title=Text(r.Title,160)},ct)
   ??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsWorkOrder>> WorkOrders(Guid c,CancellationToken ct)
 {var a=await Admin(ct);return await store.WorkOrders(a.TenantId,Id(c),ct);}
 public async Task CompleteWorkOrder(Guid c,Guid order,CancellationToken ct)
 {
  var a=await Admin(ct);
  if(!await store.CompleteWorkOrder(a.TenantId,a.SubjectId,Id(c),Id(order),ct))throw new AmsScopeException();
 }
 public async Task<AmsStock> AddStock(AmsStockCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Quantity(r.ReorderLevel,true);
  return await store.AddStock(a.TenantId,r with {Sku=Code(r.Sku,40),Name=Text(r.Name,160),
    Unit=Text(r.Unit,16)},ct)??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsStock>> Stock(Guid c,CancellationToken ct)
 {var a=await Admin(ct);return await store.Stock(a.TenantId,Id(c),ct);}
 public async Task<AmsMovement> MoveStock(AmsStockMove r,string? key,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.StockId);Quantity(r.Quantity);
  if(r.Type is not ("IN" or "OUT" or "ADJUST"))throw new ArgumentException("Invalid movement type.");
  if(r.Reference?.Length>100)throw new ArgumentException("Reference too long.");
  if(key is null||key.Length is < 8 or > 128||key.Any(c=>c is < '!' or > '~'))
   throw new ArgumentException("Idempotency-Key must be 8–128 printable ASCII characters.");
  return await store.MoveStock(a.TenantId,a.SubjectId,r,key,ct)??throw new AmsScopeException();
 }
 public async Task<AmsPurchase> AddPurchase(AmsPurchaseCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.VendorId);Id(r.StockId);
  Quantity(r.Quantity);Quantity(r.UnitPrice,true);
  if(r.Quantity*r.UnitPrice>9999999999999999.99m)
   throw new ArgumentException("Purchase total exceeds maximum.");
  return await store.AddPurchase(a.TenantId,r,ct)??throw new AmsScopeException();
 }
 public async Task<IReadOnlyList<AmsPurchase>> Purchases(Guid c,CancellationToken ct)
 {var a=await Admin(ct);return await store.Purchases(a.TenantId,Id(c),ct);}
 public async Task<AmsDashboard> Dashboard(Guid c,CancellationToken ct)
 {var a=await Admin(ct);return await store.Dashboard(a.TenantId,Id(c),ct);}
}
