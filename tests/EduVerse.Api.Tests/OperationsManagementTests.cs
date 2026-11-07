using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;
public sealed class OperationsManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),foreignTenant=Guid.NewGuid(),
  campus=Guid.NewGuid(),otherCampus=Guid.NewGuid(),admin=Guid.NewGuid(),member=Guid.NewGuid();
 private readonly string slug="ops-"+Guid.NewGuid().ToString("N"),otherSlug="ops-other-"+Guid.NewGuid().ToString("N"),
  email=Guid.NewGuid().ToString("N")+"@example.org",otherEmail=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="Operations-Test-Password-2026!";
 private WebApplicationFactory<Program>? app;
 private async Task Execute(string sql,params (string Key,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(sql,db);
  foreach(var (key,val) in args)cmd.Parameters.AddWithValue(key,val);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (id,s) in new[]{(tenant,slug),(foreignTenant,otherSlug)})
   await Execute("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Operations Test School','School')",
    ("@id",id.ToString("D")),("@slug",s));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,mail) in new[]{(admin,email),(member,otherEmail)})
   await Execute("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normal,'Operations Test User',@hash)
    """,("@id",id.ToString("D")),("@email",mail),("@normal",mail.ToUpperInvariant()),("@hash",hash));
  await Execute("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
   ("@t",tenant.ToString("D")),("@u",admin.ToString("D")));
  await Execute("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
   ("@t",foreignTenant.ToString("D")),("@u",member.ToString("D")));
  foreach(var (id,t) in new[]{(campus,tenant),(otherCampus,foreignTenant)})
   await Execute("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@t,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@t",t.ToString("D")));
  foreach(var t in new[]{tenant,foreignTenant})
   await Execute("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'PILOT')",
    ("@t",t.ToString("D")));
  app=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(
    new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
 }
 private async Task<HttpClient> Client(bool administrator=true)
 {
  var c=app!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var login=await c.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(administrator?slug:otherSlug,administrator?email:otherEmail,Password));
  Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  var t=await login.Content.ReadFromJsonAsync<LoginResponse>();
  Assert.NotNull(t);
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",t.AccessToken);
  return c;
 }
 private async Task<StudentRecord> Student(HttpClient c,string name)
 {
  using var response=await c.PostAsJsonAsync("/api/v1/students",
   new StudentCreate(campus,null,name,"Student",new DateOnly(2013,5,1)));
  Assert.Equal(HttpStatusCode.Created,response.StatusCode);
  return (await response.Content.ReadFromJsonAsync<StudentRecord>())!;
 }
 private static async Task<T> Created<T>(HttpClient c,string path,object input)
 {
  using var response=await c.PostAsJsonAsync(path,input);
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var record=await response.Content.ReadFromJsonAsync<T>();
  Assert.NotNull(record);
  return record;
 }
 private const string Base="/api/v1/operations";
 [Fact]
 public async Task LibraryLoansRequireFreeCopyAndHonorReservationQueue()
 {
  if(app is null)return;
  using var c=await Client();
  var first=await Student(c,"Asha");var second=await Student(c,"Bala");
  var title=await Created<LibraryTitle>(c,Base+"/titles",new LibraryTitleCreate(campus,"Programming 101","Author",null));
  var copy=await Created<LibraryCopy>(c,Base+"/copies",new LibraryCopyCreate(campus,title.Id,"BK-0001"));
  var waiting=await Created<LibraryReservation>(c,Base+"/reservations",
   new LibraryReservationCreate(campus,title.Id,first.Id));
  var due=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
  using var skip=await c.PostAsJsonAsync(Base+"/loans",new LibraryLoanCreate(campus,copy.Id,second.Id,due));
  Assert.Equal(HttpStatusCode.Conflict,skip.StatusCode);
  var loan=await Created<LibraryLoan>(c,Base+"/loans",new LibraryLoanCreate(campus,copy.Id,first.Id,due));
  using var duplicate=await c.PostAsJsonAsync(Base+"/loans",new LibraryLoanCreate(campus,copy.Id,second.Id,due));
  Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
  using var returned=await c.PostAsync(Base+$"/loans/{loan.Id:D}/return?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.OK,returned.StatusCode);
  var result=(await returned.Content.ReadFromJsonAsync<LibraryLoan>())!;
  Assert.Equal("RETURNED",result.Status);Assert.Equal(0m,result.Fine);
  using var again=await c.PostAsync(Base+$"/loans/{loan.Id:D}/return?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NotFound,again.StatusCode);
  using var queue=await c.GetAsync(Base+$"/reservations?campusId={campus:D}");
  Assert.Contains((await queue.Content.ReadFromJsonAsync<LibraryReservation[]>())!,x=>x.Id==waiting.Id&&x.Status=="FULFILLED");
 }
 [Fact]
 public async Task TransportAssignmentsRespectSharedVehicleCapacityAndRelease()
 {
  if(app is null)return;
  using var c=await Client();
  var a=await Student(c,"Anu");var b=await Student(c,"Bhanu");
  var vehicle=await Created<TransportVehicle>(c,Base+"/vehicles",
   new TransportVehicleCreate(campus,"TS09AB1234",1,"Driver",null));
  var route=await Created<TransportRoute>(c,Base+"/routes",
   new TransportRouteCreate(campus,vehicle.Id,"R1","North route"));
  var stop=await Created<TransportStop>(c,Base+"/stops",
   new TransportStopCreate(campus,route.Id,"Market",1));
  var assigned=await Created<TransportAssignment>(c,Base+"/route-assignments",
   new TransportAssignmentCreate(campus,a.Id,route.Id,stop.Id));
  using var full=await c.PostAsJsonAsync(Base+"/route-assignments",
   new TransportAssignmentCreate(campus,b.Id,route.Id,stop.Id));
  Assert.Equal(HttpStatusCode.Conflict,full.StatusCode);
  using var wrong=await c.PostAsJsonAsync(Base+"/route-assignments",
   new TransportAssignmentCreate(otherCampus,b.Id,route.Id,stop.Id));
  Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
  using var released=await c.PostAsync(Base+$"/route-assignments/{assigned.Id:D}/release?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,released.StatusCode);
  await Created<TransportAssignment>(c,Base+"/route-assignments",
   new TransportAssignmentCreate(campus,b.Id,route.Id,stop.Id));
 }
 [Fact]
 public async Task HostelTransferRequiresVacatingAndForbidsDoubleOccupancy()
 {
  if(app is null)return;
  using var c=await Client();
  var a=await Student(c,"Chitra");var b=await Student(c,"Deepa");
  var block=await Created<HostelBlock>(c,Base+"/hostel-blocks",
   new HostelBlockCreate(campus,"A","Girls hostel"));
  var room=await Created<HostelRoom>(c,Base+"/hostel-rooms",
   new HostelRoomCreate(campus,block.Id,"101"));
  var bed1=await Created<HostelBed>(c,Base+"/hostel-beds",new HostelBedCreate(campus,room.Id,"B1"));
  var bed2=await Created<HostelBed>(c,Base+"/hostel-beds",new HostelBedCreate(campus,room.Id,"B2"));
  var old=await Created<HostelAllocation>(c,Base+"/hostel-allocations",
   new HostelAllocationCreate(campus,a.Id,bed1.Id));
  using var duplicateBed=await c.PostAsJsonAsync(Base+"/hostel-allocations",
   new HostelAllocationCreate(campus,b.Id,bed1.Id));
  Assert.Equal(HttpStatusCode.Conflict,duplicateBed.StatusCode);
  using var duplicateStudent=await c.PostAsJsonAsync(Base+"/hostel-allocations",
   new HostelAllocationCreate(campus,a.Id,bed2.Id));
  Assert.Equal(HttpStatusCode.Conflict,duplicateStudent.StatusCode);
  using var vacated=await c.PostAsync(Base+$"/hostel-allocations/{old.Id:D}/vacate?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,vacated.StatusCode);
  await Created<HostelAllocation>(c,Base+"/hostel-allocations",
   new HostelAllocationCreate(campus,a.Id,bed2.Id));
  using var beds=await c.GetAsync(Base+$"/hostel-beds?campusId={campus:D}");
  var list=(await beds.Content.ReadFromJsonAsync<HostelBed[]>())!;
  Assert.False(list.Single(x=>x.Id==bed1.Id).Occupied);
  Assert.True(list.Single(x=>x.Id==bed2.Id).Occupied);
  using var dashboard=await c.GetAsync(Base+$"/dashboard?campusId={campus:D}");
  var stats=(await dashboard.Content.ReadFromJsonAsync<OperationsDashboard>())!;
  Assert.Equal(1,stats.HostelOccupied);Assert.Equal(2,stats.HostelBeds);
 }
 [Fact]
 public async Task CampusAndRoleIsolationProtectAllModules()
 {
  if(app is null)return;
  using var c=await Client();
  using var foreignTitles=await c.GetAsync(Base+$"/titles?campusId={otherCampus:D}");
  Assert.Equal(HttpStatusCode.OK,foreignTitles.StatusCode);
  Assert.Empty((await foreignTitles.Content.ReadFromJsonAsync<LibraryTitle[]>())!);
  using var wrongTitle=await c.PostAsJsonAsync(Base+"/titles",
   new LibraryTitleCreate(otherCampus,"Foreign","Author",null));
  Assert.Equal(HttpStatusCode.NotFound,wrongTitle.StatusCode);
  using var memberClient=await Client(false);
  using var forbidden=await memberClient.GetAsync(Base+$"/dashboard?campusId={otherCampus:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=app.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var denied=await anonymous.GetAsync(Base+$"/loans?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
 }
 public Task DisposeAsync(){app?.Dispose();return Task.CompletedTask;}
}
