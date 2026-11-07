using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Students;

public interface IStudentStore
{
    Task<bool> IsAdminAsync(TenantIdentity actor,CancellationToken ct);
    Task<AdmissionRecord?> SubmitAsync(Guid tenant,AdmissionSubmit request,CancellationToken ct);
    Task<IReadOnlyList<AdmissionRecord>> ApplicationsAsync(Guid tenant,Guid campus,CancellationToken ct);
    Task<bool> DecideAsync(Guid tenant,Guid campus,Guid id,string decision,Guid actor,CancellationToken ct);
    Task<StudentRecord?> CreateStudentAsync(Guid tenant,StudentCreate request,CancellationToken ct);
    Task<IReadOnlyList<StudentRecord>> StudentsAsync(Guid tenant,Guid campus,CancellationToken ct);
    Task<StudentRecord?> StudentAsync(Guid tenant,Guid campus,Guid id,CancellationToken ct);
    Task<GuardianRecord?> AddGuardianAsync(Guid tenant,Guid campus,Guid student,GuardianCreate request,CancellationToken ct);
    Task<IReadOnlyList<GuardianRecord>> GuardiansAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct);
    Task<EnrollmentRecord?> EnrollAsync(Guid tenant,Guid campus,Guid student,EnrollmentCreate request,CancellationToken ct);
    Task<IReadOnlyList<EnrollmentRecord>> EnrollmentsAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct);
    Task<bool> LifecycleAsync(Guid tenant,Guid campus,Guid student,LifecycleCommand request,Guid actor,CancellationToken ct);
    Task<DocumentRecord?> SaveDocumentAsync(Guid tenant,Guid campus,Guid student,string category,string filename,string mime,byte[] data,CancellationToken ct);
    Task<IReadOnlyList<DocumentRecord>> DocumentsAsync(Guid tenant,Guid campus,Guid student,CancellationToken ct);
    Task<(string Name,string Mime,byte[] Bytes)?> ReadDocumentAsync(Guid tenant,Guid campus,Guid student,Guid document,CancellationToken ct);
    Task<bool> ReviewDocumentAsync(Guid tenant,Guid campus,Guid student,Guid document,string decision,Guid actor,CancellationToken ct);
}
