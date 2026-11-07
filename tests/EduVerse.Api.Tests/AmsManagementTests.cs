using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;
public sealed class AmsManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),otherTenant=Guid.NewGuid(),
  campus=Guid.NewGuid(),foreignCampus=Guid.NewGuid(),admin=Guid.NewGuid(),member=Guid.NewGuid();
 private readonly string slug="ams-"+Guid.NewGuid().ToString("N"),
  otherSlug="other-ams-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org",
  otherEmail=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="Ams-Test-Password-2026!";
 private WebApplicationFactory<Program>? host;
 private async Task Exec(string sql,params (string Key,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(sql,db);
  foreach(var (key,val) in args)cmd.Parameters.AddWithValue(key,val);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (id,code) in new[]{(tenant,slug),(otherTenant,otherSlug)})
   await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'AMS Test School','School')",
    ("@id",id.ToString("D")),("@slug",code));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,mail) in new[]{(admin,email),(member,otherEmail)})
   await Exec("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normal,'AMS Test User',@hash)
    """,("@id",id.ToString("D")),("@email",mail),("@normal",mail.ToUpperInvariant()),("@hash",hash));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
   ("@t",tenant.ToString("D")),("@u",admin.ToString("D")));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
   ("@t",otherTenant.ToString("D")),("@u",member.ToString("D")));
  foreach(var (id,t) in new[]{(campus,tenant),(foreignCampus,otherTenant)})
   await Exec("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@t,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@t",t.ToString("D")));
  await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'STANDARD')",
   ("@t",tenant.ToString("D")));
  await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'PILOT')",
   ("@t",otherTenant.ToString("D")));
  host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
    new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
 }
 private async Task<HttpClient> Client(bool asAdmin=true)
 {
  var c=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var login=await c.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(asAdmin?slug:otherSlug,asAdmin?email:otherEmail,Password));
  Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  var t=await login.Content.ReadFromJsonAsync<LoginResponse>();Assert.NotNull(t);
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",t.AccessToken);
  return c;
 }
 private static async Task<HttpResponseMessage> Move(HttpClient c,AmsStockMove r,string key)
 {
  using var request=new HttpRequestMessage(HttpMethod.Post,"/api/v1/ams/stock/movements")
   {Content=JsonContent.Create(r)};
  request.Headers.Add("Idempotency-Key",key);
  return await c.SendAsync(request);
 }
 private async Task<(AmsCategory Category,AmsAsset Asset)> Register(HttpClient c)
 {
  using var category=await c.PostAsJsonAsync("/api/v1/ams/categories",
   new AmsCategoryCreate(campus,"IT","IT Equipment"));
  Assert.Equal(HttpStatusCode.OK,category.StatusCode);
  var cat=(await category.Content.ReadFromJsonAsync<AmsCategory>())!;
  using var asset=await c.PostAsJsonAsync("/api/v1/ams/assets",
   new AmsAssetCreate(campus,cat.Id,"Projector","SN-1001",new DateOnly(2026,10,1),15000m));
  Assert.Equal(HttpStatusCode.OK,asset.StatusCode);
  var record=(await asset.Content.ReadFromJsonAsync<AmsAsset>())!;
  return(cat,record);
 }
 [Fact]
 public async Task RegisterAllocateTransferReturnAndRetireAsset()
 {
  if(host is null)return;
  using var c=await Client();
  var (category,asset)=await Register(c);
  Assert.StartsWith("EVA-",asset.AssetCode);
  using var invalid=await c.PostAsJsonAsync("/api/v1/ams/assets/allocate",
   new AmsAllocation(foreignCampus,asset.Id,null,null,admin,"Invalid campus"));
  Assert.Equal(HttpStatusCode.NotFound,invalid.StatusCode);
  var building=Guid.NewGuid();
  await Exec("""
   INSERT INTO institution_buildings(id,tenant_id,campus_id,code,display_name)
   VALUES(@id,@t,@c,'BLK1','Building One')
   """,("@id",building.ToString("D")),("@t",tenant.ToString("D")),("@c",campus.ToString("D")));
  using var allocated=await c.PostAsJsonAsync("/api/v1/ams/assets/allocate",
   new AmsAllocation(campus,asset.Id,building,null,null,"Classroom"));
  Assert.Equal(HttpStatusCode.OK,allocated.StatusCode);
  var assigned=(await allocated.Content.ReadFromJsonAsync<AmsAsset>())!;
  Assert.Equal("ALLOCATED",assigned.Status);
  Assert.Equal(building,assigned.BuildingId);
  using var returned=await c.PostAsJsonAsync("/api/v1/ams/assets/lifecycle",
   new AmsAssetAction(campus,asset.Id,"RETURN","Returned"));
  Assert.Equal(HttpStatusCode.OK,returned.StatusCode);
  using var retired=await c.PostAsJsonAsync("/api/v1/ams/assets/lifecycle",
   new AmsAssetAction(campus,asset.Id,"RETIRE","End of life"));
  Assert.Equal(HttpStatusCode.OK,retired.StatusCode);
  using var blocked=await c.PostAsJsonAsync("/api/v1/ams/assets/allocate",
   new AmsAllocation(campus,asset.Id,building,null,null,null));
  Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
  using var disposed=await c.PostAsJsonAsync("/api/v1/ams/assets/lifecycle",
   new AmsAssetAction(campus,asset.Id,"DISPOSE","Recycled"));
  Assert.Equal(HttpStatusCode.OK,disposed.StatusCode);
  using var events=await c.GetAsync($"/api/v1/ams/assets/{asset.Id:D}/events?campusId={campus:D}");
  var history=(await events.Content.ReadFromJsonAsync<AmsAssetEvent[]>())!;
  Assert.Equal(5,history.Length);
  Assert.Contains(history,e=>e.EventCode=="REGISTERED");
 }
 [Fact]
 public async Task StockMovementsAreIdempotentAndPreventOverIssue()
 {
  if(host is null)return;
  using var c=await Client();
  using var create=await c.PostAsJsonAsync("/api/v1/ams/stock",
   new AmsStockCreate(campus,"PAPER-A4","A4 Paper","reams",3m));
  Assert.Equal(HttpStatusCode.OK,create.StatusCode);
  var item=(await create.Content.ReadFromJsonAsync<AmsStock>())!;
  var receive=new AmsStockMove(campus,item.Id,"IN",10m,"GRN-100");
  var key="receive-"+Guid.NewGuid().ToString("N");
  using var first=await Move(c,receive,key);
  using var replay=await Move(c,receive,key);
  Assert.Equal(HttpStatusCode.OK,first.StatusCode);
  Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
  var movement=(await first.Content.ReadFromJsonAsync<AmsMovement>())!;
  var duplicate=(await replay.Content.ReadFromJsonAsync<AmsMovement>())!;
  Assert.Equal(movement.Id,duplicate.Id);Assert.Equal(10m,movement.BalanceAfter);
  using var different=await Move(c,receive with {Quantity=5m},key);
  Assert.Equal(HttpStatusCode.Conflict,different.StatusCode);
  using var overIssue=await Move(c,new AmsStockMove(campus,item.Id,"OUT",11m,"REQ-101"),
   "over-"+Guid.NewGuid().ToString("N"));
  Assert.Equal(HttpStatusCode.Conflict,overIssue.StatusCode);
  using var issue=await Move(c,new AmsStockMove(campus,item.Id,"OUT",8m,"REQ-102"),
   "issue-"+Guid.NewGuid().ToString("N"));
  Assert.Equal(HttpStatusCode.OK,issue.StatusCode);
  using var stockResponse=await c.GetAsync($"/api/v1/ams/stock?campusId={campus:D}");
  var stock=(await stockResponse.Content.ReadFromJsonAsync<AmsStock[]>())!;
  Assert.Single(stock);Assert.Equal(2m,stock[0].Quantity);Assert.True(stock[0].LowStock);
  using var summaryResponse=await c.GetAsync($"/api/v1/ams/dashboard?campusId={campus:D}");
  var summary=await summaryResponse.Content.ReadFromJsonAsync<AmsDashboard>();
  Assert.Equal(1,summary?.LowStockItems);
 }
 [Fact]
 public async Task ConcurrentStockIssuesCannotBothSucceed()
 {
  if(host is null)return;
  using var c=await Client();
  using var created=await c.PostAsJsonAsync("/api/v1/ams/stock",
   new AmsStockCreate(campus,"TONER","Printer toner","pcs",1m));
  var item=(await created.Content.ReadFromJsonAsync<AmsStock>())!;
  using var received=await Move(c,new AmsStockMove(campus,item.Id,"IN",5m,null),
   "initial-"+Guid.NewGuid().ToString("N"));
  Assert.Equal(HttpStatusCode.OK,received.StatusCode);
  var issue=new AmsStockMove(campus,item.Id,"OUT",4m,null);
  var first=Move(c,issue,"concurrent-a-"+Guid.NewGuid().ToString("N"));
  var second=Move(c,issue,"concurrent-b-"+Guid.NewGuid().ToString("N"));
  var responses=await Task.WhenAll(first,second);
  try{
   Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.OK);
   Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Conflict);
  }finally{foreach(var r in responses)r.Dispose();}
 }
 [Fact]
 public async Task WorkOrdersVendorsAndPurchasesAreCampusScoped()
 {
  if(host is null)return;
  using var c=await Client();
  var (_,asset)=await Register(c);
  using var order=await c.PostAsJsonAsync("/api/v1/ams/maintenance",
   new AmsMaintenanceCreate(campus,asset.Id,"Annual check",new DateOnly(2026,12,10)));
  Assert.Equal(HttpStatusCode.OK,order.StatusCode);
  var work=(await order.Content.ReadFromJsonAsync<AmsWorkOrder>())!;
  using var wrong=await c.PostAsync(
   $"/api/v1/ams/maintenance/{work.Id:D}/complete?campusId={foreignCampus:D}",null);
  Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
  using var completed=await c.PostAsync(
   $"/api/v1/ams/maintenance/{work.Id:D}/complete?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,completed.StatusCode);
  using var vendorResp=await c.PostAsJsonAsync("/api/v1/ams/vendors",
   new AmsVendorCreate(campus,"Local Supplies","vendor@example.org"));
  var vendor=(await vendorResp.Content.ReadFromJsonAsync<AmsVendor>())!;
  using var stockResp=await c.PostAsJsonAsync("/api/v1/ams/stock",
   new AmsStockCreate(campus,"CHALK","Chalk boxes","box",10m));
  var stock=(await stockResp.Content.ReadFromJsonAsync<AmsStock>())!;
  using var purchase=await c.PostAsJsonAsync("/api/v1/ams/purchases",
   new AmsPurchaseCreate(campus,vendor.Id,stock.Id,12m,25.50m));
  Assert.Equal(HttpStatusCode.OK,purchase.StatusCode);
  var draft=(await purchase.Content.ReadFromJsonAsync<AmsPurchase>())!;
  Assert.Equal(306m,draft.Total);Assert.Equal("DRAFT",draft.Status);
  using var foreign=await c.PostAsJsonAsync("/api/v1/ams/purchases",
   new AmsPurchaseCreate(foreignCampus,vendor.Id,stock.Id,1m,10m));
  Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
 }
 [Fact]
 public async Task TenantMemberPilotAndAnonymousCannotAccessAms()
 {
  if(host is null)return;
  using var c=await Client();
  using var foreign=await c.GetAsync($"/api/v1/ams/assets?campusId={foreignCampus:D}");
  Assert.Equal(HttpStatusCode.OK,foreign.StatusCode);
  Assert.Empty((await foreign.Content.ReadFromJsonAsync<AmsAsset[]>())!);
  using var memberClient=await Client(false);
  using var forbidden=await memberClient.GetAsync($"/api/v1/ams/assets?campusId={foreignCampus:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var denied=await anonymous.GetAsync($"/api/v1/ams/assets?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
 }
 public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
