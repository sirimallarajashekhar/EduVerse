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
public sealed class ExamManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),otherTenant=Guid.NewGuid(),campus=Guid.NewGuid(),
  otherCampus=Guid.NewGuid(),admin=Guid.NewGuid(),member=Guid.NewGuid();
 private readonly string slug="exams-"+Guid.NewGuid().ToString("N"),otherSlug="other-exams-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org",
  otherEmail=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="Exam-Test-Password-2026!";
 private WebApplicationFactory<Program>? host;
 private async Task Exec(string sql,params (string Name,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(sql,db);
  foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (id,code) in new[]{(tenant,slug),(otherTenant,otherSlug)})
   await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Exams Test School','School')",
    ("@id",id.ToString("D")),("@slug",code));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,address) in new[]{(admin,email),(member,otherEmail)})
   await Exec("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normalized,'Exams Test User',@hash)
    """,("@id",id.ToString("D")),("@email",address),
    ("@normalized",address.ToUpperInvariant()),("@hash",hash));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
   ("@t",tenant.ToString("D")),("@u",admin.ToString("D")));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
   ("@t",otherTenant.ToString("D")),("@u",member.ToString("D")));
  foreach(var (id,tid) in new[]{(campus,tenant),(otherCampus,otherTenant)})
   await Exec("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@t,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@t",tid.ToString("D")));
  foreach(var tid in new[]{tenant,otherTenant})
   await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'PILOT')",
    ("@t",tid.ToString("D")));
  host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(
    new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
 }
 private async Task<HttpClient> Client(bool asAdmin=true)
 {
  var c=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var response=await c.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(asAdmin?slug:otherSlug,asAdmin?email:otherEmail,Password));
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var token=await response.Content.ReadFromJsonAsync<LoginResponse>();Assert.NotNull(token);
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.AccessToken);
  return c;
 }
 private static readonly GradeBandCreate[] Bands=[
  new("F",0m,34.99m,0m),new("C",35m,49.99m,5m),
  new("B",50m,74.99m,7m),new("A",75m,100m,10m)
 ];
 private async Task<(ExamRecord Exam,AssessmentRecord Assessment,EnrollmentRecord Enroll,StudentRecord Student)> Setup(HttpClient c)
 {
  using var yearResponse=await c.PostAsJsonAsync("/api/v1/academic/years",
   new AcademicYearCreate(campus,"2026-27","Academic 2026-27",new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
  Assert.Equal(HttpStatusCode.Created,yearResponse.StatusCode);
  var year=(await yearResponse.Content.ReadFromJsonAsync<AcademicYear>())!;
  async Task<AcademicItem> Item(string kind,string code,string name)
  {
   using var response=await c.PostAsJsonAsync("/api/v1/academic/catalog",
    new AcademicItemCreate(campus,kind,code,name,null,null,null,null,null));
   Assert.Equal(HttpStatusCode.Created,response.StatusCode);
   return (await response.Content.ReadFromJsonAsync<AcademicItem>())!;
  }
  var cls=await Item("CLASS","C09","Class Nine");
  var subject=await Item("SUBJECT","MATH","Mathematics");
  using var studentResponse=await c.PostAsJsonAsync("/api/v1/students",
   new StudentCreate(campus,null,"Sneha","Rao",new DateOnly(2013,4,1)));
  Assert.Equal(HttpStatusCode.Created,studentResponse.StatusCode);
  var student=(await studentResponse.Content.ReadFromJsonAsync<StudentRecord>())!;
  using var enrollmentResponse=await c.PostAsJsonAsync(
   $"/api/v1/students/{student.Id:D}/enrollments?campusId={campus:D}",
   new EnrollmentCreate(year.Id,cls.Id));
  Assert.Equal(HttpStatusCode.Created,enrollmentResponse.StatusCode);
  var enrollment=(await enrollmentResponse.Content.ReadFromJsonAsync<EnrollmentRecord>())!;
  using var schemeResponse=await c.PostAsJsonAsync("/api/v1/exams/schemes",
   new GradingSchemeCreate(campus,"TEN","Ten Point Scheme",Bands));
  Assert.Equal(HttpStatusCode.OK,schemeResponse.StatusCode);
  var scheme=(await schemeResponse.Content.ReadFromJsonAsync<GradingSchemeRecord>())!;
  using var examResponse=await c.PostAsJsonAsync("/api/v1/exams",
   new ExamCreate(campus,year.Id,scheme.Id,"FINAL","Annual Exam","FINAL"));
  Assert.Equal(HttpStatusCode.OK,examResponse.StatusCode);
  var exam=(await examResponse.Content.ReadFromJsonAsync<ExamRecord>())!;
  using var assessmentResponse=await c.PostAsJsonAsync("/api/v1/exams/assessments",
   new AssessmentCreate(campus,exam.Id,cls.Id,subject.Id,new DateOnly(2026,11,16),
    new TimeOnly(9,0),120,100m,35m,4m));
  Assert.Equal(HttpStatusCode.OK,assessmentResponse.StatusCode);
  var assessment=(await assessmentResponse.Content.ReadFromJsonAsync<AssessmentRecord>())!;
  return(exam,assessment,enrollment,student);
 }
 [Fact]
 public async Task GradeRangesMustBeCompleteAndNonOverlapping()
 {
  if(host is null)return;
  using var c=await Client();
  using var response=await c.PostAsJsonAsync("/api/v1/exams/schemes",
   new GradingSchemeCreate(campus,"BAD","Invalid",[
    new GradeBandCreate("A",0m,80m,10m),new GradeBandCreate("B",80m,100m,5m)
   ]));
  Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
  using var foreign=await c.PostAsJsonAsync("/api/v1/exams/schemes",
   new GradingSchemeCreate(otherCampus,"FOREIGN","Foreign",Bands));
  Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
 }
 [Fact]
 public async Task MarksAreValidatedAndPublicationLocksFutureEdits()
 {
  if(host is null)return;
  using var c=await Client();
  var setup=await Setup(c);
  using var tooHigh=await c.PostAsJsonAsync("/api/v1/exams/marks",
   new MarkCreate(campus,setup.Assessment.Id,setup.Enroll.Id,101m,false));
  Assert.Equal(HttpStatusCode.BadRequest,tooHigh.StatusCode);
  using var early=await c.PostAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/approve?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.Conflict,early.StatusCode);
  using var mark=await c.PostAsJsonAsync("/api/v1/exams/marks",
   new MarkCreate(campus,setup.Assessment.Id,setup.Enroll.Id,85.50m,false));
  Assert.Equal(HttpStatusCode.OK,mark.StatusCode);
  using var approve=await c.PostAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/approve?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,approve.StatusCode);
  using var locked=await c.PostAsJsonAsync("/api/v1/exams/marks",
   new MarkCreate(campus,setup.Assessment.Id,setup.Enroll.Id,90m,false));
  Assert.Equal(HttpStatusCode.NotFound,locked.StatusCode);
  using var unpublished=await c.GetAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/results/{setup.Enroll.Id:D}?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.NotFound,unpublished.StatusCode);
  using var publish=await c.PostAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/publish?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,publish.StatusCode);
  using var duplicatePublish=await c.PostAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/publish?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.Conflict,duplicatePublish.StatusCode);
  using var result=await c.GetAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/results/{setup.Enroll.Id:D}?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.OK,result.StatusCode);
  var report=await result.Content.ReadFromJsonAsync<ResultRecord>();
  Assert.NotNull(report);
  Assert.Equal(85.50m,report.Total);Assert.Equal(85.50m,report.Percentage);
  Assert.Equal(10m,report.Gpa);Assert.True(report.Passed);
  Assert.Single(report.Subjects);Assert.Equal("A",report.Subjects[0].Grade);
  using var analytics=await c.GetAsync($"/api/v1/exams/{setup.Exam.Id:D}/analytics?campusId={campus:D}");
  var stats=await analytics.Content.ReadFromJsonAsync<ExamAnalytics>();
  Assert.Equal(1,stats?.Students);Assert.Equal(100m,stats?.PassPercentage);
  using var cgpa=await c.GetAsync($"/api/v1/exams/students/{setup.Student.Id:D}/cgpa?campusId={campus:D}");
  var cumulative=await cgpa.Content.ReadFromJsonAsync<CgpaRecord>();
  Assert.Equal(10m,cumulative?.Cgpa);
  using var file=await c.GetAsync(
   $"/api/v1/exams/{setup.Exam.Id:D}/results/{setup.Enroll.Id:D}/download?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.OK,file.StatusCode);
  Assert.Contains(setup.Student.AdmissionNumber,await file.Content.ReadAsStringAsync());
 }
 [Fact]
 public async Task TenantAndRoleIsolationRejectsForeignCampusRecords()
 {
  if(host is null)return;
  using var c=await Client();
  var setup=await Setup(c);
  using var wrong=await c.PostAsJsonAsync("/api/v1/exams/marks",
   new MarkCreate(otherCampus,setup.Assessment.Id,setup.Enroll.Id,50m,false));
  Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
  using var list=await c.GetAsync($"/api/v1/exams?campusId={otherCampus:D}");
  Assert.Equal(HttpStatusCode.OK,list.StatusCode);
  Assert.Empty((await list.Content.ReadFromJsonAsync<ExamRecord[]>())!);
  using var member=await Client(false);
  using var forbidden=await member.GetAsync($"/api/v1/exams?campusId={otherCampus:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var denied=await anonymous.GetAsync($"/api/v1/exams?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
 }
 public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
