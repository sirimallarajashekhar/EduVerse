using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Learning;
public sealed class LearningService(ILearningStore store,ITenantContext context, EduVerse.Application.Identity.TenantReadAuthorizer reader)
{
 private async Task<TenantIdentity> Admin(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!await store.IsAdmin(actor,ct))
   throw new UnauthorizedAccessException("Active academic administrator required.");
  return actor;
 }
 private static Guid Id(Guid g)=>g==Guid.Empty?throw new ArgumentException("Identifier required."):g;
 private static string Text(string? s,int max)
 {
  s=(s??"").Trim();if(s.Length<1||s.Length>max)throw new ArgumentException("Invalid required text.");
  return s;
 }
 private static T Found<T>(T? x) where T:class=>x??throw new LearningScopeException();
 private static decimal Score(decimal d,decimal max,bool zero=false)
 {
  if(d<0||!zero&&d==0||d>max||decimal.Round(d,2)!=d)throw new ArgumentException("Invalid two-decimal score.");
  return d;
 }
 public async Task<LessonRecord> CreateLesson(LessonCreate r,CancellationToken ct)
 {
  var a=await reader.RequireAsync("learning.manage",r.CampusId,ct);Id(r.CampusId);Id(r.AcademicYearId);Id(r.ClassId);Id(r.SubjectId);
  if(r.ResourceUrl is not null && (r.ResourceUrl.Length>600 ||
      !Uri.TryCreate(r.ResourceUrl,UriKind.Absolute,out var url) || url.Scheme!="https"))
   throw new ArgumentException("Resource URL must be HTTPS.");
  return Found(await store.CreateLesson(a.TenantId,a.SubjectId,r with{Title=Text(r.Title,160),Body=Text(r.Body,8000)},ct));
 }
 public async Task<IReadOnlyList<LessonRecord>> Lessons(Guid c,CancellationToken ct)=>
  await store.Lessons((await reader.RequireAsync("learning.read",c,ct)).TenantId,Id(c),ct);
 public async Task PublishLesson(Guid c,Guid id,CancellationToken ct)
 {if(!await store.PublishLesson((await Admin(ct)).TenantId,Id(c),Id(id),ct))throw new LearningScopeException();}
 public async Task<AssignmentRecord> CreateAssignment(AssignmentCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.LessonId);
  if(r.DueUtc.Kind!=DateTimeKind.Utc||r.DueUtc<=DateTime.UtcNow)
   throw new ArgumentException("Assignment due date must be a future UTC timestamp.");
  Score(r.MaxScore,999999.99m);
  return Found(await store.CreateAssignment(a.TenantId,r with{Title=Text(r.Title,160),
   Instructions=Text(r.Instructions,8000)},ct));
 }
 public async Task<IReadOnlyList<AssignmentRecord>> Assignments(Guid c,CancellationToken ct)=>
  await store.Assignments((await reader.RequireAsync("learning.read",c,ct)).TenantId,Id(c),ct);
 public async Task PublishAssignment(Guid c,Guid id,CancellationToken ct)
 {if(!await store.PublishAssignment((await Admin(ct)).TenantId,Id(c),Id(id),ct))throw new LearningScopeException();}
 public async Task<SubmissionRecord> Submit(SubmissionCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.AssignmentId);Id(r.EnrollmentId);
  return Found(await store.Submit(a.TenantId,r with{ResponseText=Text(r.ResponseText,8000)},ct));
 }
 public async Task<IReadOnlyList<SubmissionRecord>> Submissions(Guid c,CancellationToken ct)=>
  await store.Submissions((await Admin(ct)).TenantId,Id(c),ct);
 public async Task<SubmissionRecord> Grade(Guid campus,Guid id,SubmissionGrade r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(campus);Id(id);Score(r.Score,999999.99m,true);
  return Found(await store.Grade(a.TenantId,a.SubjectId,campus,id,r with{CampusId=campus,
   Feedback=Text(r.Feedback,1000)},ct));
 }
 public async Task<StudentLearningProgress> Progress(Guid c,Guid enrollment,CancellationToken ct)=>
  Found(await store.Progress((await Admin(ct)).TenantId,Id(c),Id(enrollment),ct));
 public async Task<AnnouncementRecord> Announce(AnnouncementCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);
  if(r.Audience is not ("ALL" or "STAFF" or "GUARDIANS"))throw new ArgumentException("Invalid audience.");
  return Found(await store.Announce(a.TenantId,r with{Title=Text(r.Title,160),Body=Text(r.Body,1000)},ct));
 }
 public async Task<IReadOnlyList<AnnouncementRecord>> Announcements(Guid c,CancellationToken ct)=>
  await store.Announcements((await reader.RequireAsync("learning.read",c,ct)).TenantId,Id(c),ct);
 public async Task<ParentThreadRecord> OpenThread(ParentThreadCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.StudentId);Id(r.GuardianId);
  return Found(await store.OpenThread(a.TenantId,r with {Subject=Text(r.Subject,160)},ct));
 }
 public async Task<IReadOnlyList<ParentThreadRecord>> Threads(Guid c,CancellationToken ct)=>
  await store.Threads((await Admin(ct)).TenantId,Id(c),ct);
 public async Task<ParentMessageRecord> SendMessage(ParentMessageCreate r,CancellationToken ct)
 {
  var a=await Admin(ct);Id(r.CampusId);Id(r.ThreadId);
  return Found(await store.SendMessage(a.TenantId,a.SubjectId,r with{Body=Text(r.Body,5000)},ct));
 }
 public async Task<IReadOnlyList<ParentMessageRecord>> Messages(Guid c,Guid thread,CancellationToken ct)=>
  await store.Messages((await Admin(ct)).TenantId,Id(c),Id(thread),ct);
 public async Task<IReadOnlyList<NotificationRecord>> Notifications(Guid c,CancellationToken ct)
 {
  var a=await Admin(ct);return await store.Notifications(a.TenantId,Id(c),a.SubjectId,ct);
 }
 public async Task MarkRead(Guid c,Guid notification,CancellationToken ct)
 {
  var a=await Admin(ct);
  if(!await store.MarkRead(a.TenantId,Id(c),a.SubjectId,Id(notification),ct))throw new LearningScopeException();
 }
 public async Task<LearningDashboard> Dashboard(Guid c,CancellationToken ct)=>
  await store.Dashboard((await reader.RequireAsync("learning.read",c,ct)).TenantId,Id(c),ct);
}
