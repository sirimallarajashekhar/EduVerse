using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Learning;
public interface ILearningStore
{
 Task<bool> IsAdmin(TenantIdentity identity,CancellationToken ct);
 Task<LessonRecord?> CreateLesson(Guid tenant,string actor,LessonCreate r,CancellationToken ct);
 Task<IReadOnlyList<LessonRecord>> Lessons(Guid tenant,Guid campus,CancellationToken ct);
 Task<bool> PublishLesson(Guid tenant,Guid campus,Guid id,CancellationToken ct);
 Task<AssignmentRecord?> CreateAssignment(Guid tenant,AssignmentCreate r,CancellationToken ct);
 Task<IReadOnlyList<AssignmentRecord>> Assignments(Guid tenant,Guid campus,CancellationToken ct);
 Task<bool> PublishAssignment(Guid tenant,Guid campus,Guid id,CancellationToken ct);
 Task<SubmissionRecord?> Submit(Guid tenant,SubmissionCreate r,CancellationToken ct);
 Task<IReadOnlyList<SubmissionRecord>> Submissions(Guid tenant,Guid campus,CancellationToken ct);
 Task<SubmissionRecord?> Grade(Guid tenant,string actor,Guid campus,Guid submission,SubmissionGrade r,CancellationToken ct);
 Task<StudentLearningProgress?> Progress(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct);
 Task<AnnouncementRecord?> Announce(Guid tenant,AnnouncementCreate r,CancellationToken ct);
 Task<IReadOnlyList<AnnouncementRecord>> Announcements(Guid tenant,Guid campus,CancellationToken ct);
 Task<ParentThreadRecord?> OpenThread(Guid tenant,ParentThreadCreate r,CancellationToken ct);
 Task<IReadOnlyList<ParentThreadRecord>> Threads(Guid tenant,Guid campus,CancellationToken ct);
 Task<ParentMessageRecord?> SendMessage(Guid tenant,string actor,ParentMessageCreate r,CancellationToken ct);
 Task<IReadOnlyList<ParentMessageRecord>> Messages(Guid tenant,Guid campus,Guid thread,CancellationToken ct);
 Task<IReadOnlyList<NotificationRecord>> Notifications(Guid tenant,Guid campus,string subject,CancellationToken ct);
 Task<bool> MarkRead(Guid tenant,Guid campus,string subject,Guid id,CancellationToken ct);
 Task<LearningDashboard> Dashboard(Guid tenant,Guid campus,CancellationToken ct);
}
public sealed class LearningScopeException:Exception {}
public sealed class LearningConflictException(string message):Exception(message) {}
