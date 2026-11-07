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
public sealed class CollegeManagementTests:IAsyncLifetime
{
 private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
 private readonly Guid tenant=Guid.NewGuid(),tenantB=Guid.NewGuid(),campus=Guid.NewGuid(),
  campusB=Guid.NewGuid(),admin=Guid.NewGuid(),userB=Guid.NewGuid();
 private readonly string slug="college-"+Guid.NewGuid().ToString("N"),
  slugB="college-b-"+Guid.NewGuid().ToString("N");
 private readonly string email=Guid.NewGuid().ToString("N")+"@example.org",
  emailB=Guid.NewGuid().ToString("N")+"@example.org";
 private const string Password="College-Test-Password-2026!";
 private WebApplicationFactory<Program>? host;
 private async Task Sql(string query,params (string Key,object Value)[] args)
 {
  await using var db=new MySqlConnection(cs!);await db.OpenAsync();
  await using var cmd=new MySqlCommand(query,db);
  foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
  await cmd.ExecuteNonQueryAsync();
 }
 public async Task InitializeAsync()
 {
  if(cs is null)return;
  foreach(var (t,s) in new[]{(tenant,slug),(tenantB,slugB)})
   await Sql("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@t,@slug,'College Test Campus','College')",
    ("@t",t.ToString("D")),("@slug",s));
  var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
  foreach(var (id,mail) in new[]{(admin,email),(userB,emailB)})
   await Sql("""
    INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
    VALUES(@id,@email,@normalized,'College Test',@hash)
    """,("@id",id.ToString("D")),("@email",mail),("@normalized",mail.ToUpperInvariant()),("@hash",hash));
  await Sql("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
   ("@t",tenant.ToString("D")),("@u",admin.ToString("D")));
  await Sql("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
   ("@t",tenantB.ToString("D")),("@u",userB.ToString("D")));
  foreach(var (c,t) in new[]{(campus,tenant),(campusB,tenantB)})
   await Sql("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@t,'MAIN','Main Campus')",
    ("@id",c.ToString("D")),("@t",t.ToString("D")));
  foreach(var t in new[]{tenant,tenantB})
   await Sql("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'PILOT')",
    ("@t",t.ToString("D")));
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
  var token=await login.Content.ReadFromJsonAsync<LoginResponse>();Assert.NotNull(token);
  c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.AccessToken);
  return c;
 }
 private static async Task<T> Create<T>(HttpClient c,string path,object request,HttpStatusCode status=HttpStatusCode.OK)
 {
  using var result=await c.PostAsJsonAsync(path,request);
  Assert.Equal(status,result.StatusCode);
  var value=await result.Content.ReadFromJsonAsync<T>();Assert.NotNull(value);
  return value;
 }
 private const string Root="/api/v1/college";
 private static readonly GradeBandCreate[] Grades=[
  new("F",0m,34.99m,0m),new("C",35m,49.99m,5m),
  new("B",50m,74.99m,7m),new("A",75m,100m,10m)
 ];
 private async Task<(CollegeProgram Program,CollegeSemester Semester,CollegeCourse Course,
  AcademicYear Year,AcademicItem CatalogCourse,AcademicItem Subject)> College(HttpClient c,
  bool allowBacklogs=false,int capacity=1)
 {
  var year=await Create<AcademicYear>(c,"/api/v1/academic/years",
   new AcademicYearCreate(campus,"2026-27","Academic 2026-27",new DateOnly(2026,6,1),
    new DateOnly(2027,5,31)),HttpStatusCode.Created);
  async Task<AcademicItem> Item(string kind,string code,string name,Guid? parent=null,Guid? academicYear=null)
    =>await Create<AcademicItem>(c,"/api/v1/academic/catalog",
     new AcademicItemCreate(campus,kind,code,name,parent,academicYear,null,null,null),HttpStatusCode.Created);
  var catalogProgram=await Item("PROGRAM","BSC","Bachelor of Science");
  var catalogCourse=await Item("COURSE","BSC01","Bachelor Year One",catalogProgram.Id);
  var catalogSemester=await Item("SEMESTER","SEM01","Semester One",null,year.Id);
  var subject=await Item("SUBJECT","MATH100","College Mathematics");
  var rule=await Create<CollegeRegulation>(c,Root+"/regulations",
   new CollegeRegulationCreate(campus,"REG2026","Academic Rules",4m,3m,allowBacklogs,8));
  var program=await Create<CollegeProgram>(c,Root+"/programs",
   new CollegeProgramCreate(campus,catalogProgram.Id,rule.Id,"BSC","Bachelor of Science"));
  var semester=await Create<CollegeSemester>(c,Root+"/semesters",
   new CollegeSemesterCreate(campus,program.Id,year.Id,catalogSemester.Id,1));
  var course=await Create<CollegeCourse>(c,Root+"/courses",
   new CollegeCourseCreate(campus,program.Id,semester.Id,subject.Id,"MATH100",
    "College Mathematics","CORE",4m,capacity,null));
  return(program,semester,course,year,catalogCourse,subject);
 }
 private async Task<(StudentRecord Student,EnrollmentRecord Enrollment)> Enroll(HttpClient c,
  AcademicYear year,AcademicItem catalog,string name)
 {
  var student=await Create<StudentRecord>(c,"/api/v1/students",
   new StudentCreate(campus,null,name,"Student",new DateOnly(2007,3,1)),HttpStatusCode.Created);
  var enrollment=await Create<EnrollmentRecord>(c,
   $"/api/v1/students/{student.Id:D}/enrollments?campusId={campus:D}",
   new EnrollmentCreate(year.Id,catalog.Id),HttpStatusCode.Created);
  return(student,enrollment);
 }
 private async Task<ExamRecord> PublishExam(HttpClient c,AcademicYear year,AcademicItem catalog,
  AcademicItem subject,EnrollmentRecord enrollment,decimal marks)
 {
  var scheme=await Create<GradingSchemeRecord>(c,"/api/v1/exams/schemes",
   new GradingSchemeCreate(campus,"CGPA","Ten point college",Grades));
  var exam=await Create<ExamRecord>(c,"/api/v1/exams",
   new ExamCreate(campus,year.Id,scheme.Id,"SEMFINAL","Semester Final","SEMESTER"));
  var assessment=await Create<AssessmentRecord>(c,"/api/v1/exams/assessments",
   new AssessmentCreate(campus,exam.Id,catalog.Id,subject.Id,new DateOnly(2026,11,20),
    new TimeOnly(9,0),90,100m,35m,4m));
  using var mark=await c.PostAsJsonAsync("/api/v1/exams/marks",
   new MarkCreate(campus,assessment.Id,enrollment.Id,marks,false));
  Assert.Equal(HttpStatusCode.OK,mark.StatusCode);
  using var approve=await c.PostAsync($"/api/v1/exams/{exam.Id:D}/approve?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,approve.StatusCode);
  using var publish=await c.PostAsync($"/api/v1/exams/{exam.Id:D}/publish?campusId={campus:D}",null);
  Assert.Equal(HttpStatusCode.NoContent,publish.StatusCode);
  return exam;
 }
 [Fact]
 public async Task DegreeEnrollmentEnforcesSeatCapacityAndTenantIsolation()
 {
  if(host is null)return;
  using var c=await Client();
  var setup=await College(c,capacity:1);
  var first=await Enroll(c,setup.Year,setup.CatalogCourse,"Anusha");
  var second=await Enroll(c,setup.Year,setup.CatalogCourse,"Bhavana");
  var assigned=await Create<CollegeRegistration>(c,Root+"/registrations",
   new CollegeRegistrationCreate(campus,first.Enrollment.Id,setup.Course.Id));
  Assert.Equal("ENROLLED",assigned.Status);
  using var duplicate=await c.PostAsJsonAsync(Root+"/registrations",
   new CollegeRegistrationCreate(campus,first.Enrollment.Id,setup.Course.Id));
  Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
  using var full=await c.PostAsJsonAsync(Root+"/registrations",
   new CollegeRegistrationCreate(campus,second.Enrollment.Id,setup.Course.Id));
  Assert.Equal(HttpStatusCode.Conflict,full.StatusCode);
  using var foreign=await c.PostAsJsonAsync(Root+"/registrations",
   new CollegeRegistrationCreate(campusB,second.Enrollment.Id,setup.Course.Id));
  Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
 }
 [Fact]
 public async Task PublishedExamGradeFlowsToCreditWeightedTranscriptAndBecomesImmutable()
 {
  if(host is null)return;
  using var c=await Client();
  var setup=await College(c);
  var person=await Enroll(c,setup.Year,setup.CatalogCourse,"Charan");
  var registration=await Create<CollegeRegistration>(c,Root+"/registrations",
   new CollegeRegistrationCreate(campus,person.Enrollment.Id,setup.Course.Id));
  using var noGrade=await c.PostAsJsonAsync(Root+"/semesters/finalize",
   new CollegeFinalize(campus,person.Enrollment.Id,setup.Semester.Id));
  Assert.Equal(HttpStatusCode.Conflict,noGrade.StatusCode);
  var exam=await PublishExam(c,setup.Year,setup.CatalogCourse,setup.Subject,person.Enrollment,85.50m);
  using var mismatch=await c.PostAsJsonAsync(Root+"/grades/import",
   new CollegeGradeFromExam(campusB,registration.Id,exam.Id));
  Assert.Equal(HttpStatusCode.NotFound,mismatch.StatusCode);
  var grade=await Create<CollegeRegistration>(c,Root+"/grades/import",
   new CollegeGradeFromExam(campus,registration.Id,exam.Id));
  Assert.Equal("PASSED",grade.Status);Assert.Equal(10m,grade.GradePoints);
  using var duplicateGrade=await c.PostAsJsonAsync(Root+"/grades/import",
   new CollegeGradeFromExam(campus,registration.Id,exam.Id));
  Assert.Equal(HttpStatusCode.Conflict,duplicateGrade.StatusCode);
  var finalized=await Create<CollegeSemesterResult>(c,Root+"/semesters/finalize",
   new CollegeFinalize(campus,person.Enrollment.Id,setup.Semester.Id));
  Assert.Equal(4m,finalized.AttemptedCredits);Assert.Equal(4m,finalized.EarnedCredits);
  Assert.Equal(10m,finalized.Sgpa);Assert.Equal(0,finalized.Backlogs);
  Assert.True(finalized.ProgressionEligible);
  using var transcriptResponse=await c.GetAsync(
   $"{Root}/transcripts?campusId={campus:D}&enrollmentId={person.Enrollment.Id:D}");
  Assert.Equal(HttpStatusCode.OK,transcriptResponse.StatusCode);
  var transcript=(await transcriptResponse.Content.ReadFromJsonAsync<CollegeTranscript>())!;
  Assert.Equal(10m,transcript.Cgpa);Assert.Single(transcript.Semesters);
  using var repeat=await c.PostAsJsonAsync(Root+"/semesters/finalize",
   new CollegeFinalize(campus,person.Enrollment.Id,setup.Semester.Id));
  Assert.Equal(HttpStatusCode.Conflict,repeat.StatusCode);
 }
 [Fact]
 public async Task BacklogBlocksProgressionUnderStrictAcademicRegulation()
 {
  if(host is null)return;
  using var c=await Client();
  var setup=await College(c,capacity:3);
  var person=await Enroll(c,setup.Year,setup.CatalogCourse,"Diya");
  var registration=await Create<CollegeRegistration>(c,Root+"/registrations",
   new CollegeRegistrationCreate(campus,person.Enrollment.Id,setup.Course.Id));
  var exam=await PublishExam(c,setup.Year,setup.CatalogCourse,setup.Subject,person.Enrollment,20m);
  var grade=await Create<CollegeRegistration>(c,Root+"/grades/import",
   new CollegeGradeFromExam(campus,registration.Id,exam.Id));
  Assert.Equal("BACKLOG",grade.Status);Assert.Equal(0m,grade.GradePoints);
  var result=await Create<CollegeSemesterResult>(c,Root+"/semesters/finalize",
   new CollegeFinalize(campus,person.Enrollment.Id,setup.Semester.Id));
  Assert.Equal(1,result.Backlogs);Assert.Equal(0m,result.Sgpa);
  Assert.False(result.ProgressionEligible);
  var secondCatalog=await Create<AcademicItem>(c,"/api/v1/academic/catalog",
   new AcademicItemCreate(campus,"SEMESTER","SEM02","Semester Two",null,setup.Year.Id,null,null,null),
   HttpStatusCode.Created);
  var second=await Create<CollegeSemester>(c,Root+"/semesters",
   new CollegeSemesterCreate(campus,setup.Program.Id,setup.Year.Id,secondCatalog.Id,2));
  var nextSubject=await Create<AcademicItem>(c,"/api/v1/academic/catalog",
   new AcademicItemCreate(campus,"SUBJECT","CHEM100","College Chemistry",null,null,null,null,null),
   HttpStatusCode.Created);
  var nextCourse=await Create<CollegeCourse>(c,Root+"/courses",
   new CollegeCourseCreate(campus,setup.Program.Id,second.Id,nextSubject.Id,"CHEM100",
    "Chemistry","CORE",4m,10,null));
  using var blocked=await c.PostAsJsonAsync(Root+"/registrations",
   new CollegeRegistrationCreate(campus,person.Enrollment.Id,nextCourse.Id));
  Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
 }
 [Fact]
 public async Task AnonymousAndOtherTenantMemberCannotAccessCollegeResults()
 {
  if(host is null)return;
  using var c=await Client();
  using var otherCampusCreate=await c.PostAsJsonAsync(Root+"/regulations",
   new CollegeRegulationCreate(campusB,"OTHER","Other campus",4m,0m,false,8));
  Assert.Equal(HttpStatusCode.NotFound,otherCampusCreate.StatusCode);
  using var member=await Client(false);
  using var forbidden=await member.GetAsync($"{Root}/report?campusId={campusB:D}");
  Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
  using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
  using var denied=await anonymous.GetAsync($"{Root}/programs?campusId={campus:D}");
  Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
 }
 public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
