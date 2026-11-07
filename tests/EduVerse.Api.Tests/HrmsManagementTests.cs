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
public sealed class HrmsManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),tenantB=Guid.NewGuid(),campus=Guid.NewGuid(),
  campusB=Guid.NewGuid(),admin=Guid.NewGuid(),memberB=Guid.NewGuid();
 private readonly string slug="hr-"+Guid.NewGuid().ToString("N"),slugB="hr-other-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org",
  emailB=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="HRMS-Test-Password-2026!";
 private WebApplicationFactory<Program>? host;
 private async Task Exec(string sql,params (string Name,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(sql,db);
  foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (id,s) in new[]{(tenant,slug),(tenantB,slugB)})
   await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'HR Test School','School')",
    ("@id",id.ToString("D")),("@slug",s));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,mail) in new[]{(admin,email),(memberB,emailB)})
   await Exec("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normalized,'HR Test',@hash)
    """,("@id",id.ToString("D")),("@email",mail),("@normalized",mail.ToUpperInvariant()),("@hash",hash));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@user,'ADMIN')",
   ("@t",tenant.ToString("D")),("@user",admin.ToString("D")));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@user,'MEMBER')",
   ("@t",tenantB.ToString("D")),("@user",memberB.ToString("D")));
  foreach(var (id,t) in new[]{(campus,tenant),(campusB,tenantB)})
   await Exec("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@t,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@t",t.ToString("D")));
  await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'STANDARD')",
   ("@t",tenant.ToString("D")));
  await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'PILOT')",
   ("@t",tenantB.ToString("D")));
  host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
    new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
 }
 private async Task<HttpClient> Client(bool asAdmin=true)
 {
  var c=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var login=await c.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(asAdmin?slug:slugB,asAdmin?email:emailB,Password));
  Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  var ticket=await login.Content.ReadFromJsonAsync<LoginResponse>();
  Assert.NotNull(ticket);
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
  return c;
 }
 private async Task<HrEmployee> Hire(HttpClient c)
 {
  using var designation=await c.PostAsJsonAsync("/api/v1/hrms/designations",
   new HrDesignationCreate(campus,"TEACHER","Teacher"));
  Assert.Equal(HttpStatusCode.OK,designation.StatusCode);
  var job=(await designation.Content.ReadFromJsonAsync<HrDesignation>())!;
  using var created=await c.PostAsJsonAsync("/api/v1/hrms/employees",
   new HrEmployeeCreate(campus,"Meena","Kumar","FACULTY",new DateOnly(2026,6,1),
     admin,null,job.Id,"teacher@example.org"));
  Assert.Equal(HttpStatusCode.OK,created.StatusCode);
  return (await created.Content.ReadFromJsonAsync<HrEmployee>())!;
 }
 [Fact]
 public async Task OnboardingTransitionsAndCampusIsolation()
 {
  if(host is null)return;
  using var c=await Client();
  var employee=await Hire(c);
  Assert.StartsWith("HRE-",employee.EmployeeNumber);
  Assert.Equal("ONBOARDING",employee.Status);
  using var foreign=await c.GetAsync($"/api/v1/hrms/employees?campusId={campusB:D}");
  var list=await foreign.Content.ReadFromJsonAsync<HrEmployee[]>();
  Assert.NotNull(list);Assert.Empty(list);
  using var invalid=await c.PostAsJsonAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/lifecycle?campusId={campusB:D}",
   new HrLifecycle("ACTIVE",null));
  Assert.Equal(HttpStatusCode.NotFound,invalid.StatusCode);
  using var active=await c.PostAsJsonAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/lifecycle?campusId={campus:D}",
   new HrLifecycle("ACTIVE","Onboarded"));
  Assert.Equal(HttpStatusCode.NoContent,active.StatusCode);
  using var repeated=await c.PostAsJsonAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/lifecycle?campusId={campus:D}",
   new HrLifecycle("ACTIVE","Duplicate"));
  Assert.Equal(HttpStatusCode.Conflict,repeated.StatusCode);
 }
 [Fact]
 public async Task LeaveApprovalsAreLimitedByBalanceAndDoNotOverlap()
 {
  if(host is null)return;
  using var c=await Client();
  var employee=await Hire(c);
  using var activated=await c.PostAsJsonAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/lifecycle?campusId={campus:D}",
   new HrLifecycle("ACTIVE",null));
  Assert.Equal(HttpStatusCode.NoContent,activated.StatusCode);
  using var typeResponse=await c.PostAsJsonAsync("/api/v1/hrms/leave-types",
   new HrLeaveTypeCreate(campus,"CASUAL","Casual Leave",3m));
  Assert.Equal(HttpStatusCode.OK,typeResponse.StatusCode);
  var type=(await typeResponse.Content.ReadFromJsonAsync<HrLeaveType>())!;
  var first=new HrLeaveCreate(campus,employee.Id,type.Id,
   new DateOnly(2026,11,10),new DateOnly(2026,11,11),"Family function");
  using var submitted=await c.PostAsJsonAsync("/api/v1/hrms/leaves",first);
  Assert.Equal(HttpStatusCode.OK,submitted.StatusCode);
  var leave=(await submitted.Content.ReadFromJsonAsync<HrLeave>())!;
  using var overlap=await c.PostAsJsonAsync("/api/v1/hrms/leaves",
   first with {From=new DateOnly(2026,11,11),To=new DateOnly(2026,11,12)});
  Assert.Equal(HttpStatusCode.Conflict,overlap.StatusCode);
  using var approval=await c.PostAsJsonAsync(
   $"/api/v1/hrms/leaves/{leave.Id:D}/decision?campusId={campus:D}",
   new HrLeaveDecision("APPROVED"));
  Assert.Equal(HttpStatusCode.NoContent,approval.StatusCode);
  using var repeat=await c.PostAsJsonAsync(
   $"/api/v1/hrms/leaves/{leave.Id:D}/decision?campusId={campus:D}",
   new HrLeaveDecision("REJECTED"));
  Assert.Equal(HttpStatusCode.NotFound,repeat.StatusCode);
  using var balance=await c.GetAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/leave-balance?campusId={campus:D}&leaveTypeId={type.Id:D}&year=2026");
  var result=await balance.Content.ReadFromJsonAsync<HrLeaveBalance>();
  Assert.NotNull(result);Assert.Equal(3m,result.Allowance);
  Assert.Equal(2m,result.Used);Assert.Equal(1m,result.Remaining);
  using var second=await c.PostAsJsonAsync("/api/v1/hrms/leaves",
   first with {From=new DateOnly(2026,11,16),To=new DateOnly(2026,11,18)});
  var overBalance=(await second.Content.ReadFromJsonAsync<HrLeave>())!;
  using var blocked=await c.PostAsJsonAsync(
   $"/api/v1/hrms/leaves/{overBalance.Id:D}/decision?campusId={campus:D}",
   new HrLeaveDecision("APPROVED"));
  Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
 }
 [Fact]
 public async Task SalaryPreviewAndStaffAttendanceAreScoped()
 {
  if(host is null)return;
  using var c=await Client();
  var employee=await Hire(c);
  using var activate=await c.PostAsJsonAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/lifecycle?campusId={campus:D}",
   new HrLifecycle("ACTIVE",null));
  using var salary=await c.PostAsJsonAsync("/api/v1/hrms/salaries",
   new HrSalaryCreate(campus,employee.Id,new DateOnly(2026,11,1),20000m,5000m,1500m));
  Assert.Equal(HttpStatusCode.OK,salary.StatusCode);
  using var preview=await c.PostAsync(
   $"/api/v1/hrms/payroll/preview?campusId={campus:D}&employeeId={employee.Id:D}&month=2026-11-01",null);
  Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
  var pay=await preview.Content.ReadFromJsonAsync<HrPayrollPreview>();
  Assert.NotNull(pay);Assert.Equal(25000m,pay.Gross);Assert.Equal(23500m,pay.Net);
  using var cross=await c.PostAsync(
   $"/api/v1/hrms/payroll/preview?campusId={campusB:D}&employeeId={employee.Id:D}&month=2026-11-01",null);
  Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
  using var attendance=await c.PostAsJsonAsync("/api/v1/attendance/staff",
   new StaffAttendanceMark(campus,admin,new DateOnly(2026,11,9),"PRESENT",null));
  Assert.Equal(HttpStatusCode.OK,attendance.StatusCode);
  using var report=await c.GetAsync($"/api/v1/hrms/attendance?campusId={campus:D}&date=2026-11-09");
  var entries=await report.Content.ReadFromJsonAsync<HrStaffAttendance[]>();
  Assert.NotNull(entries);Assert.Single(entries);Assert.Equal(employee.Id,entries[0].EmployeeId);
 }
 [Fact]
 public async Task PrivateDocumentsAndUnauthorizedAccessAreProtected()
 {
  if(host is null)return;
  using var c=await Client();
  var employee=await Hire(c);
  var bytes=new byte[]{137,80,78,71,13,10,26,10,0,0,0,0};
  using var form=new MultipartFormDataContent();
  var file=new ByteArrayContent(bytes);
  file.Headers.ContentType=new MediaTypeHeaderValue("image/png");
  form.Add(file,"file","certificate.png");
  using var uploaded=await c.PostAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/documents?campusId={campus:D}&type=QUALIFICATION",form);
  Assert.Equal(HttpStatusCode.OK,uploaded.StatusCode);
  var doc=await uploaded.Content.ReadFromJsonAsync<HrDocument>();
  Assert.NotNull(doc);
  using var downloaded=await c.GetAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/documents/{doc.Id:D}/download?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.OK,downloaded.StatusCode);
  Assert.Equal(bytes,await downloaded.Content.ReadAsByteArrayAsync());
  using var wrongCampus=await c.GetAsync(
   $"/api/v1/hrms/employees/{employee.Id:D}/documents/{doc.Id:D}/download?campusId={campusB:D}");
  Assert.Equal(HttpStatusCode.NotFound,wrongCampus.StatusCode);
  using var other=await Client(false);
  using var forbidden=await other.GetAsync($"/api/v1/hrms/employees?campusId={campusB:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var denied=await anonymous.GetAsync($"/api/v1/hrms/employees?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
 }
 public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
