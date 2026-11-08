using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.College;
public sealed class CollegeService(ICollegeStore store,ITenantContext context, EduVerse.Application.Identity.TenantReadAuthorizer reader)
{
 private async Task<TenantIdentity> Admin(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!await store.IsAcademicAdmin(actor,ct))
   throw new UnauthorizedAccessException("Active academic administrator required.");
  return actor;
 }
 private static Guid Id(Guid id)=>id==Guid.Empty?throw new ArgumentException("Identifier required."):id;
 private static string Name(string? text,int max)
 {
  var s=(text??"").Trim();
  if(s.Length is < 2 or > 160||s.Length>max)throw new ArgumentException("Invalid name.");
  return s;
 }
 private static string Code(string? text)
 {
  var s=Name(text,30).ToUpperInvariant();
  if(!s.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
   throw new ArgumentException("Code must use alphanumeric or '-' characters.");
  return s;
 }
 private static decimal Decimal(decimal d,decimal max,bool allowZero=false)
 {
  if(d<0||!allowZero&&d==0||d>max||decimal.Round(d,2)!=d)
   throw new ArgumentException("Invalid two-decimal value.");
  return d;
 }
 private static T Found<T>(T? item) where T:class=>item??throw new CollegeScopeException();
 public async Task<CollegeRegulation> CreateRegulation(CollegeRegulationCreate r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);
  Decimal(r.PassingPoints,10,true);Decimal(r.MinCreditsForProgression,200,true);
  if(r.MaxSemesters is <2 or >16)throw new ArgumentException("Semesters must be between 2 and 16.");
  return Found(await store.CreateRegulation(actor.TenantId,r with {Code=Code(r.Code),Name=Name(r.Name,160)},ct));
 }
 public async Task<IReadOnlyList<CollegeRegulation>> Regulations(Guid campus,CancellationToken ct)=>
  await store.Regulations((await reader.RequireAsync("college.read",campus,ct)).TenantId,Id(campus),ct);
 public async Task<CollegeProgram> CreateProgram(CollegeProgramCreate r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);Id(r.CatalogProgramId);Id(r.RegulationId);
  return Found(await store.CreateProgram(actor.TenantId,r with {Code=Code(r.Code),Name=Name(r.Name,160)},ct));
 }
 public async Task<IReadOnlyList<CollegeProgram>> Programs(Guid campus,CancellationToken ct)=>
  await store.Programs((await reader.RequireAsync("college.read",campus,ct)).TenantId,Id(campus),ct);
 public async Task<CollegeSemester> CreateSemester(CollegeSemesterCreate r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);Id(r.ProgramId);Id(r.AcademicYearId);Id(r.CatalogSemesterId);
  if(r.Number is <1 or >16)throw new ArgumentException("Invalid semester number.");
  return Found(await store.CreateSemester(actor.TenantId,r,ct));
 }
 public async Task<IReadOnlyList<CollegeSemester>> Semesters(Guid campus,CancellationToken ct)=>
  await store.Semesters((await reader.RequireAsync("college.read",campus,ct)).TenantId,Id(campus),ct);
 public async Task<CollegeCourse> CreateCourse(CollegeCourseCreate r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);Id(r.ProgramId);Id(r.SemesterId);Id(r.SubjectId);
  if(r.PrerequisiteCourseId is Guid pre)Id(pre);
  if(r.Type is not ("CORE" or "ELECTIVE")||r.Capacity is <1 or >5000)
   throw new ArgumentException("Invalid course type or capacity.");
  Decimal(r.Credits,30);
  return Found(await store.CreateCourse(actor.TenantId,r with {Code=Code(r.Code),Title=Name(r.Title,160)},ct));
 }
 public async Task<IReadOnlyList<CollegeCourse>> Courses(Guid campus,CancellationToken ct)=>
  await store.Courses((await reader.RequireAsync("college.read",campus,ct)).TenantId,Id(campus),ct);
 public async Task<CollegeRegistration> Register(CollegeRegistrationCreate r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);Id(r.EnrollmentId);Id(r.CourseId);
  return Found(await store.Register(actor.TenantId,r,ct));
 }
 public async Task<IReadOnlyList<CollegeRegistration>> Registrations(Guid campus,Guid enrollment,CancellationToken ct)=>
  await store.Registrations((await Admin(ct)).TenantId,Id(campus),Id(enrollment),ct);
 public async Task<CollegeRegistration> ImportGrade(CollegeGradeFromExam r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);Id(r.RegistrationId);Id(r.ExamId);
  return Found(await store.ImportGrade(actor.TenantId,r,ct));
 }
 public async Task<CollegeSemesterResult> Finalize(CollegeFinalize r,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(r.CampusId);Id(r.EnrollmentId);Id(r.SemesterId);
  return Found(await store.Finalize(actor.TenantId,actor.SubjectId,r,ct));
 }
 public async Task<CollegeTranscript> Transcript(Guid campus,Guid enrollment,CancellationToken ct)=>
  Found(await store.Transcript((await Admin(ct)).TenantId,Id(campus),Id(enrollment),ct));
 public async Task<CollegeReport> Report(Guid campus,CancellationToken ct)=>
  await store.Report((await reader.RequireAsync("college.read",campus,ct)).TenantId,Id(campus),ct);
}
