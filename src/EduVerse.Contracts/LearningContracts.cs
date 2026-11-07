namespace EduVerse.Contracts;

public sealed record LessonCreate(Guid CampusId,Guid AcademicYearId,Guid ClassId,Guid SubjectId,string Title,string Body,string? ResourceUrl);
public sealed record LessonRecord(Guid Id,Guid AcademicYearId,Guid ClassId,Guid SubjectId,string Title,string Body,string? ResourceUrl,string Status);
public sealed record AssignmentCreate(Guid CampusId,Guid LessonId,string Title,string Instructions,DateTime DueUtc,decimal MaxScore);
public sealed record AssignmentRecord(Guid Id,Guid LessonId,string Title,string Instructions,DateTime DueUtc,decimal MaxScore,string Status);
public sealed record SubmissionCreate(Guid CampusId,Guid AssignmentId,Guid EnrollmentId,string ResponseText);
public sealed record SubmissionRecord(Guid Id,Guid AssignmentId,Guid EnrollmentId,string ResponseText,string Status,decimal? Score,string? Feedback);
public sealed record SubmissionGrade(Guid CampusId,decimal Score,string Feedback);
public sealed record AnnouncementCreate(Guid CampusId,string Title,string Body,string Audience);
public sealed record AnnouncementRecord(Guid Id,string Title,string Body,string Audience,DateTime PublishedUtc);
public sealed record ParentThreadCreate(Guid CampusId,Guid StudentId,Guid GuardianId,string Subject);
public sealed record ParentThreadRecord(Guid Id,Guid StudentId,Guid GuardianId,string Subject,string Status);
public sealed record ParentMessageCreate(Guid CampusId,Guid ThreadId,string Body);
public sealed record ParentMessageRecord(Guid Id,Guid ThreadId,string Author,string Body,DateTime CreatedUtc);
public sealed record NotificationRecord(Guid Id,string Title,string Body,string Status,DateTime DeliveredUtc,DateTime? ReadUtc);
public sealed record LearningDashboard(int Lessons,int PublishedAssignments,int Submitted,int Graded,decimal AveragePercentage,int Announcements,int Threads,int UnreadNotifications);
public sealed record StudentLearningProgress(Guid EnrollmentId,int Assignments,int Submitted,int Graded,decimal AveragePercentage);
