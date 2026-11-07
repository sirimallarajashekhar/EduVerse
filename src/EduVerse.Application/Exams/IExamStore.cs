using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Exams;
public interface IExamStore
{
 Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct);
 Task<GradingSchemeRecord?> CreateSchemeAsync(Guid tenant,GradingSchemeCreate request,CancellationToken ct);
 Task<IReadOnlyList<GradingSchemeRecord>> SchemesAsync(Guid tenant,Guid campus,CancellationToken ct);
 Task<ExamRecord?> CreateExamAsync(Guid tenant,ExamCreate request,CancellationToken ct);
 Task<IReadOnlyList<ExamRecord>> ExamsAsync(Guid tenant,Guid campus,CancellationToken ct);
 Task<AssessmentRecord?> AddAssessmentAsync(Guid tenant,AssessmentCreate request,CancellationToken ct);
 Task<IReadOnlyList<AssessmentRecord>> AssessmentsAsync(Guid tenant,Guid campus,Guid exam,CancellationToken ct);
 Task<MarkRecord?> EnterMarkAsync(Guid tenant,Guid actor,MarkCreate request,CancellationToken ct);
 Task<IReadOnlyList<MarkRecord>> MarksAsync(Guid tenant,Guid campus,Guid assessment,CancellationToken ct);
 Task<bool> TransitionAsync(Guid tenant,Guid actor,Guid campus,Guid exam,string action,CancellationToken ct);
 Task<ResultRecord?> ResultAsync(Guid tenant,Guid campus,Guid exam,Guid enrollment,CancellationToken ct);
 Task<ExamAnalytics?> AnalyticsAsync(Guid tenant,Guid campus,Guid exam,CancellationToken ct);
 Task<CgpaRecord?> CgpaAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct);
}
public sealed class ExamScopeException:Exception { }
public sealed class ExamConflictException(string message):Exception(message) { }
