using EduVerse.Contracts;
using EduVerse.Domain;
namespace EduVerse.Application.College;
public interface ICollegeStore
{
 Task<bool> IsAcademicAdmin(TenantIdentity actor,CancellationToken ct);
 Task<CollegeRegulation?> CreateRegulation(Guid tenant,CollegeRegulationCreate r,CancellationToken ct);
 Task<IReadOnlyList<CollegeRegulation>> Regulations(Guid tenant,Guid campus,CancellationToken ct);
 Task<CollegeProgram?> CreateProgram(Guid tenant,CollegeProgramCreate r,CancellationToken ct);
 Task<IReadOnlyList<CollegeProgram>> Programs(Guid tenant,Guid campus,CancellationToken ct);
 Task<CollegeSemester?> CreateSemester(Guid tenant,CollegeSemesterCreate r,CancellationToken ct);
 Task<IReadOnlyList<CollegeSemester>> Semesters(Guid tenant,Guid campus,CancellationToken ct);
 Task<CollegeCourse?> CreateCourse(Guid tenant,CollegeCourseCreate r,CancellationToken ct);
 Task<IReadOnlyList<CollegeCourse>> Courses(Guid tenant,Guid campus,CancellationToken ct);
 Task<CollegeRegistration?> Register(Guid tenant,CollegeRegistrationCreate r,CancellationToken ct);
 Task<IReadOnlyList<CollegeRegistration>> Registrations(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct);
 Task<CollegeRegistration?> ImportGrade(Guid tenant,CollegeGradeFromExam r,CancellationToken ct);
 Task<CollegeSemesterResult?> Finalize(Guid tenant,string actor,CollegeFinalize r,CancellationToken ct);
 Task<CollegeTranscript?> Transcript(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct);
 Task<CollegeReport> Report(Guid tenant,Guid campus,CancellationToken ct);
}
public sealed class CollegeScopeException:Exception {}
public sealed class CollegeConflictException(string message):Exception(message) {}
