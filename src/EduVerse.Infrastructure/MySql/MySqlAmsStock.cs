using System.Security.Cryptography;
using System.Text;
using EduVerse.Application.Ams;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Infrastructure.MySql;
public sealed partial class MySqlAmsStore
{
 public async Task<AmsStock?> AddStock(Guid tenant,AmsStockCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Campus(db,tx,tenant,r.CampusId,ct))return null;
  var id=Guid.NewGuid();
  await using var cmd=new MySqlCommand("""
   INSERT INTO ams_stock_items(id,tenant_id,campus_id,sku,display_name,unit,reorder_level)
   VALUES(@id,@t,@c,@sku,@name,@unit,@level)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@sku",r.Sku),
   ("@name",r.Name),("@unit",r.Unit),("@level",r.ReorderLevel));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new AmsStock(id,r.CampusId,r.Sku,r.Name,r.Unit,0,r.ReorderLevel,true);
 }
 public async Task<IReadOnlyList<AmsStock>> Stock(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<AmsStock>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,campus_id,sku,display_name,unit,quantity,reorder_level FROM ams_stock_items
   WHERE tenant_id=@t AND campus_id=@c ORDER BY sku LIMIT 500
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var reader=await cmd.ExecuteReaderAsync(ct);
  while(await reader.ReadAsync(ct))
  {
   var qty=reader.GetDecimal(5);var level=reader.GetDecimal(6);
   list.Add(new AmsStock(reader.GetGuid(0),reader.GetGuid(1),reader.GetString(2),
    reader.GetString(3),reader.GetString(4),qty,level,qty<=level));
  }
  return list;
 }
 private static byte[] Hash(AmsStockMove r)=>SHA256.HashData(Encoding.UTF8.GetBytes(
  string.Join("\n",Id(r.CampusId),Id(r.StockId),r.Type,
   r.Quantity.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),r.Reference??"")));
 public async Task<AmsMovement?> MoveStock(Guid tenant,string actor,AmsStockMove r,string key,CancellationToken ct)
 {
  var hash=Hash(r);
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  // Tenant row lock serializes duplicate submissions and concurrent stock changes,
  // including simultaneous keys that target the same stock item.
  await LockTenant(db,tx,tenant,ct);
  await using(var replay=new MySqlCommand("""
   SELECT id,stock_item_id,movement_type,quantity,balance_after,request_sha256
   FROM ams_stock_movements WHERE tenant_id=@t AND idempotency_key=@key
   """,db,tx))
  {
   Add(replay,("@t",Id(tenant)),("@key",key));
   await using var rd=await replay.ExecuteReaderAsync(ct);
   if(await rd.ReadAsync(ct))
   {
    if(!CryptographicOperations.FixedTimeEquals(hash,(byte[])rd.GetValue(5)))
     throw new AmsConflictException("Idempotency-Key was previously used for different details.");
    var saved=new AmsMovement(rd.GetGuid(0),rd.GetGuid(1),rd.GetString(2),
     rd.GetDecimal(3),rd.GetDecimal(4));
    await rd.DisposeAsync();
    // Verify original tenant+campus+item matches request, including on retry.
    if(saved.StockId!=r.StockId)return null;
    await tx.CommitAsync(ct);return saved;
   }
  }
  decimal current;
  await using(var find=new MySqlCommand("""
   SELECT quantity FROM ams_stock_items WHERE tenant_id=@t AND campus_id=@c AND id=@stock FOR UPDATE
   """,db,tx))
  {
   Add(find,("@t",Id(tenant)),("@c",Id(r.CampusId)),("@stock",Id(r.StockId)));
   var raw=await find.ExecuteScalarAsync(ct);
   if(raw is null)return null;
   current=Convert.ToDecimal(raw);
  }
  // ADJUST is a positive correction/addition in this foundation. No untracked negative adjustment.
  var result=r.Type=="OUT"?current-r.Quantity:current+r.Quantity;
  if(result<0)throw new AmsConflictException("Stock issue exceeds available quantity.");
  if(result>9999999999999999.99m)throw new AmsConflictException("Stock balance exceeds supported range.");
  await using(var change=new MySqlCommand("""
   UPDATE ams_stock_items SET quantity=@balance
   WHERE tenant_id=@t AND campus_id=@c AND id=@stock
   """,db,tx))
  {
   Add(change,("@balance",result),("@t",Id(tenant)),("@c",Id(r.CampusId)),("@stock",Id(r.StockId)));
   await change.ExecuteNonQueryAsync(ct);
  }
  var id=Guid.NewGuid();
  await using(var record=new MySqlCommand("""
   INSERT INTO ams_stock_movements(id,tenant_id,campus_id,stock_item_id,movement_type,
    quantity,balance_after,reference_code,idempotency_key,request_sha256,actor_id)
   VALUES(@id,@t,@c,@stock,@type,@quantity,@balance,@reference,@key,@hash,@actor)
   """,db,tx))
  {
   Add(record,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@stock",Id(r.StockId)),("@type",r.Type),("@quantity",r.Quantity),
    ("@balance",result),("@reference",r.Reference),("@key",key),("@hash",hash),
    ("@actor",actor));
   await record.ExecuteNonQueryAsync(ct);
  }
  await tx.CommitAsync(ct);
  return new AmsMovement(id,r.StockId,r.Type,r.Quantity,result);
 }
 public async Task<AmsPurchase?> AddPurchase(Guid tenant,AmsPurchaseCreate r,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
  if(!await Exists(db,tx,"""
   SELECT EXISTS(SELECT 1 FROM ams_vendors v JOIN ams_stock_items s
     ON s.tenant_id=v.tenant_id AND s.campus_id=v.campus_id
     WHERE v.id=@vendor AND s.id=@stock AND v.tenant_id=@t AND v.campus_id=@c)
   """,ct,("@t",Id(tenant)),("@c",Id(r.CampusId)),
    ("@vendor",Id(r.VendorId)),("@stock",Id(r.StockId))))return null;
  var id=Guid.NewGuid();var total=r.Quantity*r.UnitPrice;
  await using var cmd=new MySqlCommand("""
   INSERT INTO ams_purchase_orders(id,tenant_id,campus_id,vendor_id,stock_item_id,
    ordered_quantity,unit_price,total_price)
   VALUES(@id,@t,@c,@vendor,@stock,@quantity,@price,@total)
   """,db,tx);
  Add(cmd,("@id",Id(id)),("@t",Id(tenant)),("@c",Id(r.CampusId)),
   ("@vendor",Id(r.VendorId)),("@stock",Id(r.StockId)),
   ("@quantity",r.Quantity),("@price",r.UnitPrice),("@total",total));
  await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);
  return new AmsPurchase(id,r.VendorId,r.StockId,r.Quantity,r.UnitPrice,total,"DRAFT");
 }
 public async Task<IReadOnlyList<AmsPurchase>> Purchases(Guid tenant,Guid campus,CancellationToken ct)
 {
  var list=new List<AmsPurchase>();
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT id,vendor_id,stock_item_id,ordered_quantity,unit_price,total_price,status
   FROM ams_purchase_orders WHERE tenant_id=@t AND campus_id=@c
   ORDER BY created_utc DESC LIMIT 250
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var rd=await cmd.ExecuteReaderAsync(ct);
  while(await rd.ReadAsync(ct))list.Add(new AmsPurchase(rd.GetGuid(0),rd.GetGuid(1),
   rd.GetGuid(2),rd.GetDecimal(3),rd.GetDecimal(4),rd.GetDecimal(5),rd.GetString(6)));
  return list;
 }
 public async Task<AmsDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct)
 {
  await using var db=Connect();await db.OpenAsync(ct);
  await using var cmd=new MySqlCommand("""
   SELECT
    (SELECT COUNT(*) FROM ams_assets WHERE tenant_id=@t AND campus_id=@c),
    (SELECT COUNT(*) FROM ams_assets WHERE tenant_id=@t AND campus_id=@c AND status='ALLOCATED'),
    (SELECT COUNT(*) FROM ams_maintenance WHERE tenant_id=@t AND campus_id=@c AND status='OPEN'),
    (SELECT COUNT(*) FROM ams_stock_items WHERE tenant_id=@t AND campus_id=@c AND quantity<=reorder_level),
    (SELECT COUNT(*) FROM ams_purchase_orders WHERE tenant_id=@t AND campus_id=@c)
   """,db);
  Add(cmd,("@t",Id(tenant)),("@c",Id(campus)));
  await using var rd=await cmd.ExecuteReaderAsync(ct);await rd.ReadAsync(ct);
  return new AmsDashboard(Convert.ToInt32(rd.GetInt64(0)),Convert.ToInt32(rd.GetInt64(1)),
   Convert.ToInt32(rd.GetInt64(2)),Convert.ToInt32(rd.GetInt64(3)),Convert.ToInt32(rd.GetInt64(4)));
 }
}
