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

/// <summary>MySQL 8 integration tests, JWT sessions, tenant isolation and accounting invariants.</summary>
public sealed class FinanceManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),foreignTenant=Guid.NewGuid(),
  campus=Guid.NewGuid(),foreignCampus=Guid.NewGuid(),admin=Guid.NewGuid(),foreignUser=Guid.NewGuid();
 private readonly string slug="fee-"+Guid.NewGuid().ToString("N"),
  otherSlug="other-fee-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org",
  otherEmail=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="Finance-Test-Password-2026!";
 private WebApplicationFactory<Program>? host;
 private async Task Exec(string sql,params (string Key,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(sql,db);
  foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (id,code) in new[]{(tenant,slug),(foreignTenant,otherSlug)})
   await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Finance Test School','School')",
    ("@id",id.ToString("D")),("@slug",code));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,address) in new[]{(admin,email),(foreignUser,otherEmail)})
   await Exec("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normalized,'Finance Test User',@hash)
    """,("@id",id.ToString("D")),("@email",address),
    ("@normalized",address.ToUpperInvariant()),("@hash",hash));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@tenant,@user,'ADMIN')",
   ("@tenant",tenant.ToString("D")),("@user",admin.ToString("D")));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@tenant,@user,'MEMBER')",
   ("@tenant",foreignTenant.ToString("D")),("@user",foreignUser.ToString("D")));
  foreach(var (id,tid) in new[]{(campus,tenant),(foreignCampus,foreignTenant)})
   await Exec("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@tenant,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@tenant",tid.ToString("D")));
  foreach(var tid in new[]{tenant,foreignTenant})
   await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@tenant,'PILOT')",
    ("@tenant",tid.ToString("D")));
  host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,configuration)=>
    configuration.AddInMemoryCollection(new Dictionary<string,string?>
     {["ConnectionStrings:EduVerse"]=cs})));
 }
 private async Task<HttpClient> Client(bool administrator=true)
 {
  var client=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var login=await client.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(administrator?slug:otherSlug,administrator?email:otherEmail,Password));
  Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  var ticket=await login.Content.ReadFromJsonAsync<LoginResponse>();
  Assert.NotNull(ticket);
  client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
  return client;
 }

 private async Task<(FeeStructureRecord Fee,ChargeRecord Charge,InstallmentRecord First,InstallmentRecord Second)> Setup(HttpClient client)
 {
  using var yearResponse=await client.PostAsJsonAsync("/api/v1/academic/years",
   new AcademicYearCreate(campus,"2026-27","Academic Year 2026-27",new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
  Assert.Equal(HttpStatusCode.Created,yearResponse.StatusCode);
  var year=(await yearResponse.Content.ReadFromJsonAsync<AcademicYear>())!;
  using var schoolClass=await client.PostAsJsonAsync("/api/v1/academic/catalog",
   new AcademicItemCreate(campus,"CLASS","C10","Class Ten",null,null,null,null,null));
  Assert.Equal(HttpStatusCode.Created,schoolClass.StatusCode);
  var classroom=(await schoolClass.Content.ReadFromJsonAsync<AcademicItem>())!;
  using var studentResponse=await client.PostAsJsonAsync("/api/v1/students",
   new StudentCreate(campus,null,"Rohan","Sharma",new DateOnly(2014,4,5)));
  Assert.Equal(HttpStatusCode.Created,studentResponse.StatusCode);
  var student=(await studentResponse.Content.ReadFromJsonAsync<StudentRecord>())!;
  using var enrolled=await client.PostAsJsonAsync(
   $"/api/v1/students/{student.Id:D}/enrollments?campusId={campus:D}",
   new EnrollmentCreate(year.Id,classroom.Id));
  Assert.Equal(HttpStatusCode.Created,enrolled.StatusCode);
  var enrollment=(await enrolled.Content.ReadFromJsonAsync<EnrollmentRecord>())!;
  using var feeResponse=await client.PostAsJsonAsync("/api/v1/finance/fees",
   new FeeStructureCreate(campus,year.Id,"TUITION","Tuition 2026-27",1200.00m));
  Assert.Equal(HttpStatusCode.OK,feeResponse.StatusCode);
  var fee=(await feeResponse.Content.ReadFromJsonAsync<FeeStructureRecord>())!;
  using var assigned=await client.PostAsJsonAsync("/api/v1/finance/charges",
   new ChargeCreate(campus,enrollment.Id,fee.Id,new[]{
    new InstallmentInput(1,new DateOnly(2026,7,1),600.00m),
    new InstallmentInput(2,new DateOnly(2026,12,1),600.00m)
   }));
  Assert.Equal(HttpStatusCode.OK,assigned.StatusCode);
  var charge=(await assigned.Content.ReadFromJsonAsync<ChargeRecord>())!;
  using var installmentsResponse=await client.GetAsync(
   $"/api/v1/finance/charges/{charge.Id:D}/installments?campusId={campus:D}");
  var installments=(await installmentsResponse.Content.ReadFromJsonAsync<InstallmentRecord[]>())!;
  Assert.Equal(2,installments.Length);
  return(fee,charge,installments[0],installments[1]);
 }
 private static async Task<HttpResponseMessage> PostWithKey(HttpClient client,string path,object payload,string key)
 {
  using var request=new HttpRequestMessage(HttpMethod.Post,path)
   {Content=JsonContent.Create(payload)};
  request.Headers.Add("Idempotency-Key",key);
  return await client.SendAsync(request);
 }
 [Fact]
 public async Task PaymentRetryReturnsSameReceiptAndPreventsOverpayment()
 {
  if(host is null)return;
  using var client=await Client();
  var setup=await Setup(client);
  var key="receipt-"+Guid.NewGuid().ToString("N");
  var payment=new PaymentCreate(campus,setup.First.Id,250.50m,"CASH","CASH-01");
  using var first=await PostWithKey(client,"/api/v1/finance/payments",payment,key);
  using var replay=await PostWithKey(client,"/api/v1/finance/payments",payment,key);
  Assert.Equal(HttpStatusCode.OK,first.StatusCode);
  Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
  var recorded=await first.Content.ReadFromJsonAsync<PaymentRecord>();
  var repeated=await replay.Content.ReadFromJsonAsync<PaymentRecord>();
  Assert.NotNull(recorded);Assert.NotNull(repeated);
  Assert.Equal(recorded.Id,repeated.Id);
  Assert.Equal(recorded.ReceiptNumber,repeated.ReceiptNumber);
  using var different=await PostWithKey(client,"/api/v1/finance/payments",
   payment with {Amount=251.50m},key);
  Assert.Equal(HttpStatusCode.Conflict,different.StatusCode);
  using var excess=await PostWithKey(client,"/api/v1/finance/payments",
   payment with {Amount=600.00m},"different-"+Guid.NewGuid().ToString("N"));
  Assert.Equal(HttpStatusCode.Conflict,excess.StatusCode);
  using var receipt=await client.GetAsync(
   $"/api/v1/finance/payments/{recorded.Id:D}/receipt?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.OK,receipt.StatusCode);
  var details=await receipt.Content.ReadFromJsonAsync<ReceiptRecord>();
  Assert.Equal(recorded.ReceiptNumber,details?.ReceiptNumber);
  Assert.Equal(250.50m,details?.Amount);
  using var listing=await client.GetAsync($"/api/v1/finance/payments?campusId={campus:D}");
  var results=(await listing.Content.ReadFromJsonAsync<PaymentRecord[]>())!;
  Assert.Single(results);
 }
 [Fact]
 public async Task ScholarshipReducesOutstandingAndRefundsAreIdempotent()
 {
  if(host is null)return;
  using var client=await Client();
  var setup=await Setup(client);
  using var concession=await client.PostAsJsonAsync("/api/v1/finance/concessions",
   new ConcessionCreate(campus,setup.Charge.Id,"SCHOLARSHIP","Academic merit",200.00m));
  Assert.Equal(HttpStatusCode.OK,concession.StatusCode);
  var key="collect-"+Guid.NewGuid().ToString("N");
  using var paid=await PostWithKey(client,"/api/v1/finance/payments",
   new PaymentCreate(campus,setup.First.Id,500m,"BANK_TRANSFER","BANK-001"),key);
  Assert.Equal(HttpStatusCode.OK,paid.StatusCode);
  var payment=(await paid.Content.ReadFromJsonAsync<PaymentRecord>())!;
  var refund=new RefundCreate(campus,payment.Id,100.00m,"Duplicate transfer","REF-001");
  var refundKey="refund-"+Guid.NewGuid().ToString("N");
  using var first=await PostWithKey(client,"/api/v1/finance/refunds",refund,refundKey);
  using var replay=await PostWithKey(client,"/api/v1/finance/refunds",refund,refundKey);
  Assert.Equal(HttpStatusCode.OK,first.StatusCode);
  Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
  var returned=(await first.Content.ReadFromJsonAsync<RefundRecord>())!;
  var again=(await replay.Content.ReadFromJsonAsync<RefundRecord>())!;
  Assert.Equal(returned.Id,again.Id);
  using var excess=await PostWithKey(client,"/api/v1/finance/refunds",
   refund with {Amount=450m},"refund-new-"+Guid.NewGuid().ToString("N"));
  Assert.Equal(HttpStatusCode.Conflict,excess.StatusCode);
  using var dashboard=await client.GetAsync($"/api/v1/finance/dashboard?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.OK,dashboard.StatusCode);
  var summary=(await dashboard.Content.ReadFromJsonAsync<FinanceDashboard>())!;
  Assert.Equal(1200m,summary.Gross);
  Assert.Equal(200m,summary.Concessions);
  Assert.Equal(500m,summary.Collected);
  Assert.Equal(100m,summary.Refunded);
  Assert.Equal(600m,summary.Outstanding);
  using var reconcile=await client.PostAsJsonAsync("/api/v1/finance/reconciliation",
   new ReconcileRequest(campus,payment.Id,"MATCHED","BANK-001"));
  Assert.Equal(HttpStatusCode.NoContent,reconcile.StatusCode);
  using var againReconcile=await client.PostAsJsonAsync("/api/v1/finance/reconciliation",
   new ReconcileRequest(campus,payment.Id,"DISPUTED",null));
  Assert.Equal(HttpStatusCode.NotFound,againReconcile.StatusCode);
 }
 [Fact]
 public async Task TenantCampusAndRoleBoundariesBlockFinancialRecords()
 {
  if(host is null)return;
  using var adminClient=await Client();
  var setup=await Setup(adminClient);
  using var wrongCampus=await adminClient.GetAsync(
   $"/api/v1/finance/charges/{setup.Charge.Id:D}/installments?campusId={foreignCampus:D}");
  Assert.Equal(HttpStatusCode.OK,wrongCampus.StatusCode);
  Assert.Empty((await wrongCampus.Content.ReadFromJsonAsync<InstallmentRecord[]>())!);
  using var wrongPayment=await PostWithKey(adminClient,"/api/v1/finance/payments",
   new PaymentCreate(foreignCampus,setup.First.Id,10m,"CASH",null),
   "foreign-"+Guid.NewGuid().ToString("N"));
  Assert.Equal(HttpStatusCode.NotFound,wrongPayment.StatusCode);
  using var member=await Client(false);
  using var forbidden=await member.GetAsync($"/api/v1/finance/dashboard?campusId={foreignCampus:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var unauthorized=await anonymous.GetAsync($"/api/v1/finance/fees?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,unauthorized.StatusCode);
 }
 public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
