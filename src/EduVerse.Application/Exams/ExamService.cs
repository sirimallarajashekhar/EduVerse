using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Exams;
/// <summary>Exam marks and grading decisions are only performed inside the authenticated API.</summary>
public sealed class ExamService(IExamStore store,ITenantContext context, EduVerse.Application.Identity.TenantReadAuthorizer reader)
{
 private async Task<TenantIdentity> Admin(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!Guid.TryParse(actor.SubjectId,out _)||
     !await store.IsAdminAsync(actor,ct))throw new UnauthorizedAccessException("Active tenant ADMIN and academic entitlement required.");
  return actor;
 }
 private static Guid Id(Guid value)=>value==Guid.Empty?throw new ArgumentException("Identifier required."):value;
 private static string Text(string? value,int max)
 {
  var s=(value??"").Trim();
  if(s.Length is < 1 or > 160||s.Length>max)throw new ArgumentException("Invalid text.");
  return s;
 }
 private static decimal Number(decimal n,decimal max,bool zero=true)
 {
  if(n<0||(!zero&&n==0)||n>max||decimal.Round(n,2)!=n)
   throw new ArgumentException("Marks/grade values must be valid two-decimal values.");
  return n;
 }
 public async Task<GradingSchemeRecord> CreateScheme(GradingSchemeCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(input.CampusId);
  var code=Text(input.Code,30).ToUpperInvariant();
  if(!code.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
   throw new ArgumentException("Invalid scheme code.");
  if(input.Bands is null||input.Bands.Count is < 1 or > 20)throw new ArgumentException("Provide 1-20 grade bands.");
  var bands=input.Bands.Select(x=>new GradeBandCreate(Text(x.Grade,10),Number(x.MinPercent,100),
    Number(x.MaxPercent,100),Number(x.Points,10))).ToArray();
  if(bands.Select(x=>x.Grade).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=bands.Length)
   throw new ArgumentException("Duplicate grade.");
  // Decimal thresholds must cover 0.00 through 100.00 with no overlaps or gaps (0.01 granularity).
  var ordered=bands.OrderBy(x=>x.MinPercent).ToArray();
  if(ordered[0].MinPercent!=0||ordered[^1].MaxPercent!=100)
   throw new ArgumentException("Grading bands must cover 0 to 100 percent.");
  for(var i=0;i<ordered.Length;i++)
  {
   if(ordered[i].MinPercent>ordered[i].MaxPercent||
      i>0&&ordered[i].MinPercent!=ordered[i-1].MaxPercent+0.01m)
    throw new ArgumentException("Grading bands must be contiguous and non-overlapping.");
  }
  return await store.CreateSchemeAsync(actor.TenantId,input with {Code=code,Name=Text(input.Name,120),Bands=ordered},ct)
   ??throw new ExamScopeException();
 }
 public async Task<IReadOnlyList<GradingSchemeRecord>> Schemes(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("exams.read",campus,ct);return await store.SchemesAsync(actor.TenantId,Id(campus),ct);
 }
 public async Task<ExamRecord> CreateExam(ExamCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(input.CampusId);Id(input.AcademicYearId);Id(input.SchemeId);
  if(input.Type is not ("UNIT" or "MIDTERM" or "FINAL" or "SEMESTER" or "PRACTICAL" or "OTHER"))
   throw new ArgumentException("Unsupported exam type.");
  var code=Text(input.Code,30).ToUpperInvariant();
  if(!code.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
   throw new ArgumentException("Invalid exam code.");
  return await store.CreateExamAsync(actor.TenantId,input with {Code=code,Title=Text(input.Title,160)},ct)
   ??throw new ExamScopeException();
 }
 public async Task<IReadOnlyList<ExamRecord>> Exams(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("exams.read",campus,ct);return await store.ExamsAsync(actor.TenantId,Id(campus),ct);
 }
 public async Task<AssessmentRecord> AddAssessment(AssessmentCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(input.CampusId);Id(input.ExamId);Id(input.AcademicItemId);Id(input.SubjectId);
  if(input.ScheduledOn==default||input.DurationMinutes is < 1 or > 600)
   throw new ArgumentException("Invalid schedule.");
  var max=Number(input.MaxMarks,999999.99m,false);
  var pass=Number(input.PassMarks,max);
  var credits=Number(input.Credits,50,false);
  return await store.AddAssessmentAsync(actor.TenantId,input with {MaxMarks=max,PassMarks=pass,Credits=credits},ct)
   ??throw new ExamScopeException();
 }
 public async Task<IReadOnlyList<AssessmentRecord>> Assessments(Guid campus,Guid exam,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("exams.read",campus,ct);return await store.AssessmentsAsync(actor.TenantId,Id(campus),Id(exam),ct);
 }
 public async Task<MarkRecord> EnterMark(MarkCreate input,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(input.CampusId);Id(input.AssessmentId);Id(input.EnrollmentId);
  if(input.IsAbsent&&input.Marks.HasValue||!input.IsAbsent&&!input.Marks.HasValue)
   throw new ArgumentException("Absent students have no numeric marks; present students require marks.");
  if(input.Marks.HasValue)Number(input.Marks.Value,999999.99m);
  return await store.EnterMarkAsync(actor.TenantId,Guid.Parse(actor.SubjectId),input,ct)
   ??throw new ExamScopeException();
 }
 public async Task<IReadOnlyList<MarkRecord>> Marks(Guid campus,Guid assessment,CancellationToken ct)
 {
  var actor=await Admin(ct);return await store.MarksAsync(actor.TenantId,Id(campus),Id(assessment),ct);
 }
 public async Task Transition(Guid campus,Guid exam,string action,CancellationToken ct)
 {
  var actor=await Admin(ct);Id(campus);Id(exam);
  if(action is not ("APPROVE" or "PUBLISH"))throw new ArgumentException("Unsupported transition.");
  if(!await store.TransitionAsync(actor.TenantId,Guid.Parse(actor.SubjectId),campus,exam,action,ct))
   throw new ExamScopeException();
 }
 public async Task<ResultRecord> Result(Guid campus,Guid exam,Guid enrollment,CancellationToken ct)
 {
  var actor=await Admin(ct);
  return await store.ResultAsync(actor.TenantId,Id(campus),Id(exam),Id(enrollment),ct)
   ??throw new ExamScopeException();
 }
 public async Task<ExamAnalytics> Analytics(Guid campus,Guid exam,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("exams.read",campus,ct);
  return await store.AnalyticsAsync(actor.TenantId,Id(campus),Id(exam),ct)
   ??throw new ExamScopeException();
 }
 public async Task<CgpaRecord> Cgpa(Guid campus,Guid student,CancellationToken ct)
 {
  var actor=await Admin(ct);
  return await store.CgpaAsync(actor.TenantId,Id(campus),Id(student),ct)
   ??throw new ExamScopeException();
 }
}
