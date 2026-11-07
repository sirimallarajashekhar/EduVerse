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

/// <summary>Integration tests run against CI MySQL 8 and real tenant-scoped JWT authentication.</summary>
public sealed class AttendanceManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),foreignTenant=Guid.NewGuid(),
  campus=Guid.NewGuid(),foreignCampus=Guid.NewGuid(),admin=Guid.NewGuid(),foreignMember=Guid.NewGuid();
 private readonly string slug="att-"+Guid.NewGuid().ToString("N"),foreignSlug="foreign-att-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org";
 private readonly string foreignEmail=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="Test-Attendance-Password-2026!";
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
  foreach(var (tid,s,k) in new[]{(tenant,slug,"School"),(foreignTenant,foreignSlug,"College")})
   await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Attendance Test',@kind)",
    ("@id",tid.ToString("D")),("@slug",s),("@kind",k));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (user,address) in new[]{(admin,email),(foreignMember,foreignEmail)})
   await Exec("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normal,'Attendance Operator',@hash)
    """,("@id",user.ToString("D")),("@email",address),("@normal",address.ToUpperInvariant()),("@hash",hash));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
   ("@t",tenant.ToString("D")),("@u",admin.ToString("D")));
  await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
   ("@t",foreignTenant.ToString("D")),("@u",foreignMember.ToString("D")));
  foreach(var (id,tid) in new[]{(campus,tenant),(foreignCampus,foreignTenant)})
   await Exec("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@tenant,'MAIN','Main Campus')",
    ("@id",id.ToString("D")),("@tenant",tid.ToString("D")));
  foreach(var tid in new[]{tenant,foreignTenant})
   await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@id,'PILOT')",
    ("@id",tid.ToString("D")));
  host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
   web.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(
    new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
 }
 private async Task<HttpClient> ClientAsync(bool isAdmin=true)
 {
  var client=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var result=await client.PostAsJsonAsync("/api/v1/identity/login",
   new LoginRequest(isAdmin?slug:foreignSlug,isAdmin?email:foreignEmail,Password));
  Assert.Equal(HttpStatusCode.OK,result.StatusCode);
  var ticket=await result.Content.ReadFromJsonAsync<LoginResponse>();
  Assert.NotNull(ticket);
  client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
  return client;
 }
 private static readonly DateOnly Day=new(2026,11,9); // Monday within 2026/27 academic year

 private async Task<(Guid Year,Guid Class,Guid Subject,Guid Student,Guid Enrollment)> SetupAcademicAsync(HttpClient client)
 {
  using var yearResponse=await client.PostAsJsonAsync("/api/v1/academic/years",
   new AcademicYearCreate(campus,"2026-27","Academic 2026/27",new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
  Assert.Equal(HttpStatusCode.Created,yearResponse.StatusCode);
  var year=(await yearResponse.Content.ReadFromJsonAsync<AcademicYear>())!;
  async Task<AcademicItem> Item(string kind,string code,string name)
  {
   using var result=await client.PostAsJsonAsync("/api/v1/academic/catalog",
    new AcademicItemCreate(campus,kind,code,name,null,null,null,null,null));
   Assert.Equal(HttpStatusCode.Created,result.StatusCode);
   return (await result.Content.ReadFromJsonAsync<AcademicItem>())!;
  }
  var classroom=await Item("CLASS","C08","Class Eight");
  var subject=await Item("SUBJECT","SCI","Science");
  using var assigned=await client.PostAsJsonAsync("/api/v1/academic/faculty-assignments",
   new FacultyAssignmentCreate(campus,subject.Id,year.Id,admin));
  Assert.Equal(HttpStatusCode.Created,assigned.StatusCode);
  using var studentResponse=await client.PostAsJsonAsync("/api/v1/students",
   new StudentCreate(campus,null,"Kiran","Rao",new DateOnly(2014,6,1)));
  Assert.Equal(HttpStatusCode.Created,studentResponse.StatusCode);
  var student=(await studentResponse.Content.ReadFromJsonAsync<StudentRecord>())!;
  using var enrolled=await client.PostAsJsonAsync(
   $"/api/v1/students/{student.Id:D}/enrollments?campusId={campus:D}",
   new EnrollmentCreate(year.Id,classroom.Id));
  Assert.Equal(HttpStatusCode.Created,enrolled.StatusCode);
  var enrollment=(await enrolled.Content.ReadFromJsonAsync<EnrollmentRecord>())!;
  return (year.Id,classroom.Id,subject.Id,student.Id,enrollment.Id);
 }
 [Fact]
 public async Task PeriodsAndTeacherSlotsRejectOverlapsAndForeignCampus()
 {
  if(host is null)return;
  using var client=await ClientAsync();
  var setup=await SetupAcademicAsync(client);
  async Task<PeriodRecord> Period(string code,int start,int end)
  {
   using var result=await client.PostAsJsonAsync("/api/v1/attendance/periods",
    new PeriodCreate(campus,code,code+" period",new TimeOnly(start,0),new TimeOnly(end,0)));
   Assert.Equal(HttpStatusCode.OK,result.StatusCode);
   return (await result.Content.ReadFromJsonAsync<PeriodRecord>())!;
  }
  var first=await Period("P01",9,10);
  var overlapping=await Period("P02",9,11);
  using var slot=await client.PostAsJsonAsync("/api/v1/attendance/timetable",
   new TimetableCreate(campus,setup.Year,setup.Class,setup.Subject,first.Id,admin,1));
  Assert.Equal(HttpStatusCode.OK,slot.StatusCode);
  using var conflicting=await client.PostAsJsonAsync("/api/v1/attendance/timetable",
   new TimetableCreate(campus,setup.Year,setup.Class,setup.Subject,overlapping.Id,admin,1));
  Assert.Equal(HttpStatusCode.Conflict,conflicting.StatusCode);
  using var foreign=await client.PostAsJsonAsync("/api/v1/attendance/periods",
   new PeriodCreate(foreignCampus,"P01","Foreign period",new TimeOnly(9,0),new TimeOnly(10,0)));
  Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
  using var other=await client.GetAsync($"/api/v1/attendance/timetable?campusId={foreignCampus:D}&academicYearId={setup.Year:D}");
  Assert.Equal(HttpStatusCode.OK,other.StatusCode);
  Assert.Empty((await other.Content.ReadFromJsonAsync<TimetableRecord[]>())!);
  using var mark=await client.PostAsJsonAsync("/api/v1/attendance/students",
   new StudentAttendanceMark(campus,setup.Enrollment,Day,first.Id,"PRESENT",null));
  Assert.Equal(HttpStatusCode.OK,mark.StatusCode);
  using var unscheduled=await client.PostAsJsonAsync("/api/v1/attendance/students",
   new StudentAttendanceMark(campus,setup.Enrollment,Day,overlapping.Id,"PRESENT",null));
  Assert.Equal(HttpStatusCode.NotFound,unscheduled.StatusCode);
 }
 [Fact]
 public async Task DailyAttendanceIsIdempotentAndHolidayAware()
 {
  if(host is null)return;
  using var client=await ClientAsync();
  var setup=await SetupAcademicAsync(client);
  async Task<HttpResponseMessage> Mark(string status)=>
   await client.PostAsJsonAsync("/api/v1/attendance/students",
    new StudentAttendanceMark(campus,setup.Enrollment,Day,null,status,null));
  using var first=await Mark("PRESENT");
  using var second=await Mark("ABSENT");
  Assert.Equal(HttpStatusCode.OK,first.StatusCode);
  Assert.Equal(HttpStatusCode.OK,second.StatusCode);
  using var marks=await client.GetAsync($"/api/v1/attendance/students?campusId={campus:D}&enrollmentId={setup.Enrollment:D}");
  var all=await marks.Content.ReadFromJsonAsync<AttendanceRecord[]>();
  Assert.NotNull(all);Assert.Single(all);Assert.Equal("ABSENT",all[0].Status);
  using var summary=await client.GetAsync($"/api/v1/attendance/students/summary?campusId={campus:D}&enrollmentId={setup.Enrollment:D}");
  var stats=await summary.Content.ReadFromJsonAsync<AttendanceSummary>();
  Assert.NotNull(stats);Assert.Equal(1,stats.Total);Assert.Equal(1,stats.Absent);
  using var holiday=await client.PostAsJsonAsync("/api/v1/academic/calendar",
   new AcademicEventCreate(campus,setup.Year,"HOLIDAY","Founders Day",new DateOnly(2026,11,10),new DateOnly(2026,11,10),null));
  Assert.Equal(HttpStatusCode.Created,holiday.StatusCode);
  using var blocked=await client.PostAsJsonAsync("/api/v1/attendance/students",
   new StudentAttendanceMark(campus,setup.Enrollment,new DateOnly(2026,11,10),null,"PRESENT",null));
  Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
  using var staffHoliday=await client.PostAsJsonAsync("/api/v1/attendance/staff",
   new StaffAttendanceMark(campus,admin,new DateOnly(2026,11,10),"PRESENT",null));
  Assert.Equal(HttpStatusCode.Conflict,staffHoliday.StatusCode);
 }
 [Fact]
 public async Task StaffAttendanceIsScopedAndRepeatable()
 {
  if(host is null)return;
  using var client=await ClientAsync();
  using var first=await client.PostAsJsonAsync("/api/v1/attendance/staff",
   new StaffAttendanceMark(campus,admin,Day,"LATE","Traffic delay"));
  Assert.Equal(HttpStatusCode.OK,first.StatusCode);
  using var update=await client.PostAsJsonAsync("/api/v1/attendance/staff",
   new StaffAttendanceMark(campus,admin,Day,"PRESENT",null));
  Assert.Equal(HttpStatusCode.OK,update.StatusCode);
  using var staff=await client.GetAsync($"/api/v1/attendance/staff?campusId={campus:D}&date={Day:yyyy-MM-dd}");
  var records=await staff.Content.ReadFromJsonAsync<StaffAttendanceRecord[]>();
  Assert.NotNull(records);Assert.Single(records);Assert.Equal("PRESENT",records[0].Status);
  using var crossCampus=await client.PostAsJsonAsync("/api/v1/attendance/staff",
   new StaffAttendanceMark(foreignCampus,admin,Day,"PRESENT",null));
  Assert.Equal(HttpStatusCode.NotFound,crossCampus.StatusCode);
 }
 [Fact]
 public async Task AnonymousAndForeignTenantMemberCannotReadOrWriteAttendance()
 {
  if(host is null)return;
  using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var unauth=await anonymous.GetAsync($"/api/v1/attendance/periods?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,unauth.StatusCode);
  using var member=await ClientAsync(false);
  using var blocked=await member.GetAsync($"/api/v1/attendance/dashboard?campusId={foreignCampus:D}");
  Assert.Equal(HttpStatusCode.Forbidden,blocked.StatusCode);
  using var cannotWrite=await member.PostAsJsonAsync("/api/v1/attendance/staff",
   new StaffAttendanceMark(foreignCampus,foreignMember,Day,"PRESENT",null));
  Assert.Equal(HttpStatusCode.Forbidden,cannotWrite.StatusCode);
 }
 public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
