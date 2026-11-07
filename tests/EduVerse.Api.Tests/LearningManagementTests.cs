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

public sealed class LearningManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),otherTenant=Guid.NewGuid(),
  campus=Guid.NewGuid(),otherCampus=Guid.NewGuid(),admin=Guid.NewGuid(),member=Guid.NewGuid();
 private readonly string slug="learn-"+Guid.NewGuid().ToString("N"),
  otherSlug="learn-other-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org",
  otherEmail=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="Learning-Test-Password-2026!";
 private WebApplicationFactory<Program>? app;
 private async Task Execute(string sql,params (string Name,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(sql,db);
  foreach(var (key,val) in args)cmd.Parameters.AddWithValue(key,val);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (id,s) in new[]{(tenant,slug),(otherTenant,otherSlug)})
   await Execute("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Learning Test School','School')",
    ("@id",id.ToString("D")),("@slug",s));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,mail) in new[]{(admin,email),(member,otherEmail)})
   await Execute("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normal,'Learning Test',@hash)
    """,("@id",id.ToString("D")),("@email",mail),("@normal",mail.ToUpperInvariant()),("@hash",hash));
  await Execute("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
   ("@t",tenant.ToString("D")),("@u",admin.ToString("D")));
  await Execute("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
   ("@t",otherTenant.ToString("D")),("@u",member.ToString("D")));
  foreach(var (id,t) in new[]{(campus,tenant),(otherCampus,otherTenant)})
   await Execute("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@t,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@t",t.ToString("D")));
  foreach(var t in new[]{tenant,otherTenant})
   await Execute("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'PILOT')",
    ("@t",t.ToString("D")));
  app=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,cfg)=>cfg.AddInMemoryCollection(
    new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
 }
 private async Task<HttpClient> Client(bool asAdmin=true)
 {
  var c=app!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var login=await c.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(asAdmin?slug:otherSlug,asAdmin?email:otherEmail,Password));
  Assert.Equal(HttpStatusCode.OK,login.StatusCode);
  var token=await login.Content.ReadFromJsonAsync<LoginResponse>();
  Assert.NotNull(token);
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.AccessToken);
  return c;
 }
 private static async Task<T> Make<T>(HttpClient c,string path,object payload)
 {
  using var response=await c.PostAsJsonAsync(path,payload);
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);
  var result=await response.Content.ReadFromJsonAsync<T>();
  Assert.NotNull(result);
  return result;
 }
 private async Task<(LessonRecord Lesson,AssignmentRecord Assignment,EnrollmentRecord Enrollment,
   StudentRecord Student,GuardianRecord Guardian)> Setup(HttpClient c)
 {
  using var yr=await c.PostAsJsonAsync("/api/v1/academic/years",
   new AcademicYearCreate(campus,"2026-27","Academic 2026-27",new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
  Assert.Equal(HttpStatusCode.Created,yr.StatusCode);
  var year=(await yr.Content.ReadFromJsonAsync<AcademicYear>())!;
  async Task<AcademicItem> Catalog(string kind,string code)
  {
   using var response=await c.PostAsJsonAsync("/api/v1/academic/catalog",
    new AcademicItemCreate(campus,kind,code,code,null,null,null,null,null));
   Assert.Equal(HttpStatusCode.Created,response.StatusCode);
   return (await response.Content.ReadFromJsonAsync<AcademicItem>())!;
  }
  var cls=await Catalog("CLASS","NINTH");
  var subject=await Catalog("SUBJECT","SCIENCE");
  using var studentResponse=await c.PostAsJsonAsync("/api/v1/students",
   new StudentCreate(campus,null,"Chandana","K",new DateOnly(2013,4,1)));
  Assert.Equal(HttpStatusCode.Created,studentResponse.StatusCode);
  var student=(await studentResponse.Content.ReadFromJsonAsync<StudentRecord>())!;
  using var enrollResponse=await c.PostAsJsonAsync($"/api/v1/students/{student.Id:D}/enrollments?campusId={campus:D}",
   new EnrollmentCreate(year.Id,cls.Id));
  Assert.Equal(HttpStatusCode.Created,enrollResponse.StatusCode);
  var enrollment=(await enrollResponse.Content.ReadFromJsonAsync<EnrollmentRecord>())!;
  using var guardianResponse=await c.PostAsJsonAsync($"/api/v1/students/{student.Id:D}/guardians?campusId={campus:D}",
   new GuardianCreate("Parent K","MOTHER",null,null,true));
  Assert.Equal(HttpStatusCode.Created,guardianResponse.StatusCode);
  var guardian=(await guardianResponse.Content.ReadFromJsonAsync<GuardianRecord>())!;
  var lesson=await Make<LessonRecord>(c,"/api/v1/learning/lessons",
   new LessonCreate(campus,year.Id,cls.Id,subject.Id,"Science chapter one",
    "Science lesson content","https://example.org/lesson"));
  using var published=await c.PostAsync($"/api/v1/learning/lessons/{lesson.Id:D}/publish?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,published.StatusCode);
  var assignment=await Make<AssignmentRecord>(c,"/api/v1/learning/assignments",
   new AssignmentCreate(campus,lesson.Id,"Chapter one practice","Explain the experiment",
    DateTime.UtcNow.AddDays(7),100m));
  return(lesson,assignment,enrollment,student,guardian);
 }
 [Fact]
 public async Task AssignmentPublicationSubmissionGradingAndProgress()
 {
  if(app is null)return;
  using var c=await Client();
  var s=await Setup(c);
  using var early=await c.PostAsJsonAsync("/api/v1/learning/submissions",
   new SubmissionCreate(campus,s.Assignment.Id,s.Enrollment.Id,"My explanation"));
  Assert.Equal(HttpStatusCode.NotFound,early.StatusCode);
  using var published=await c.PostAsync(
   $"/api/v1/learning/assignments/{s.Assignment.Id:D}/publish?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,published.StatusCode);
  var submission=await Make<SubmissionRecord>(c,"/api/v1/learning/submissions",
   new SubmissionCreate(campus,s.Assignment.Id,s.Enrollment.Id,"My explanation"));
  using var replay=await c.PostAsJsonAsync("/api/v1/learning/submissions",
   new SubmissionCreate(campus,s.Assignment.Id,s.Enrollment.Id,"Another answer"));
  Assert.Equal(HttpStatusCode.Conflict,replay.StatusCode);
  using var excessive=await c.PostAsJsonAsync(
   $"/api/v1/learning/submissions/{submission.Id:D}/grade?campusId={campus:D}",
   new SubmissionGrade(campus,101m,"Too high"));
  Assert.Equal(HttpStatusCode.BadRequest,excessive.StatusCode);
  using var graded=await c.PostAsJsonAsync(
   $"/api/v1/learning/submissions/{submission.Id:D}/grade?campusId={campus:D}",
   new SubmissionGrade(campus,88.50m,"Good research"));
  Assert.Equal(HttpStatusCode.OK,graded.StatusCode);
  var grade=(await graded.Content.ReadFromJsonAsync<SubmissionRecord>())!;
  Assert.Equal("GRADED",grade.Status);Assert.Equal(88.50m,grade.Score);
  using var regrade=await c.PostAsJsonAsync(
   $"/api/v1/learning/submissions/{submission.Id:D}/grade?campusId={campus:D}",
   new SubmissionGrade(campus,89m,"Changed"));
  Assert.Equal(HttpStatusCode.NotFound,regrade.StatusCode);
  using var progressResponse=await c.GetAsync(
   $"/api/v1/learning/enrollments/{s.Enrollment.Id:D}/progress?campusId={campus:D}");
  var progress=(await progressResponse.Content.ReadFromJsonAsync<StudentLearningProgress>())!;
  Assert.Equal(1,progress.Assignments);Assert.Equal(1,progress.Submitted);
  Assert.Equal(1,progress.Graded);Assert.Equal(88.50m,progress.AveragePercentage);
  using var dashboard=await c.GetAsync($"/api/v1/learning/dashboard?campusId={campus:D}");
  var stats=(await dashboard.Content.ReadFromJsonAsync<LearningDashboard>())!;
  Assert.Equal(1,stats.Graded);
 }
 [Fact]
 public async Task ParentThreadsRequireActualStudentGuardianRelationship()
 {
  if(app is null)return;
  using var c=await Client();
  var s=await Setup(c);
  using var invalid=await c.PostAsJsonAsync("/api/v1/learning/threads",
   new ParentThreadCreate(otherCampus,s.Student.Id,s.Guardian.Id,"Cross-campus thread"));
  Assert.Equal(HttpStatusCode.NotFound,invalid.StatusCode);
  var thread=await Make<ParentThreadRecord>(c,"/api/v1/learning/threads",
   new ParentThreadCreate(campus,s.Student.Id,s.Guardian.Id,"Learning update"));
  var message=await Make<ParentMessageRecord>(c,"/api/v1/learning/messages",
   new ParentMessageCreate(campus,thread.Id,"Please discuss reading progress."));
  Assert.Equal(thread.Id,message.ThreadId);
  using var list=await c.GetAsync($"/api/v1/learning/threads/{thread.Id:D}/messages?campusId={campus:D}");
  Assert.Single((await list.Content.ReadFromJsonAsync<ParentMessageRecord[]>())!);
  using var wrong=await c.GetAsync($"/api/v1/learning/threads/{thread.Id:D}/messages?campusId={otherCampus:D}");
  Assert.Empty((await wrong.Content.ReadFromJsonAsync<ParentMessageRecord[]>())!);
 }
 [Fact]
 public async Task AnnouncementsCreateScopedInAppDeliveryAndReadReceipt()
 {
  if(app is null)return;
  using var c=await Client();
  var news=await Make<AnnouncementRecord>(c,"/api/v1/learning/announcements",
   new AnnouncementCreate(campus,"School assembly","Assembly starts at 9:00","ALL"));
  using var inboxResponse=await c.GetAsync($"/api/v1/learning/notifications?campusId={campus:D}");
  var inbox=(await inboxResponse.Content.ReadFromJsonAsync<NotificationRecord[]>())!;
  Assert.Single(inbox);Assert.Equal("DELIVERED",inbox[0].Status);
  using var wrong=await c.PostAsync(
   $"/api/v1/learning/notifications/{inbox[0].Id:D}/read?campusId={otherCampus:D}",null);
  Assert.Equal(HttpStatusCode.NotFound,wrong.StatusCode);
  using var marked=await c.PostAsync(
   $"/api/v1/learning/notifications/{inbox[0].Id:D}/read?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,marked.StatusCode);
  using var duplicate=await c.PostAsync(
   $"/api/v1/learning/notifications/{inbox[0].Id:D}/read?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NotFound,duplicate.StatusCode);
  using var announcements=await c.GetAsync($"/api/v1/learning/announcements?campusId={campus:D}");
  Assert.Contains((await announcements.Content.ReadFromJsonAsync<AnnouncementRecord[]>())!,x=>x.Id==news.Id);
 }
 [Fact]
 public async Task TenantRoleAndAnonymousIsolation()
 {
  if(app is null)return;
  using var c=await Client();
  using var cross=await c.PostAsJsonAsync("/api/v1/learning/announcements",
   new AnnouncementCreate(otherCampus,"Foreign","Incorrect campus","ALL"));
  Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
  using var wrongCampus=await c.GetAsync($"/api/v1/learning/lessons?campusId={otherCampus:D}");
  Assert.Empty((await wrongCampus.Content.ReadFromJsonAsync<LessonRecord[]>())!);
  using var memberClient=await Client(false);
  using var forbidden=await memberClient.GetAsync($"/api/v1/learning/dashboard?campusId={otherCampus:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=app.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var denied=await anonymous.GetAsync($"/api/v1/learning/lessons?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
 }
 public Task DisposeAsync(){app?.Dispose();return Task.CompletedTask;}
}
