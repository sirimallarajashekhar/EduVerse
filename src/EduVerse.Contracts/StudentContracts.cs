namespace EduVerse.Contracts;

public sealed record AdmissionSubmit(Guid CampusId,string GivenName,string FamilyName,DateOnly DateOfBirth,string? Email,string? Phone);
public sealed record AdmissionRecord(Guid Id,Guid TenantId,Guid CampusId,long ApplicationNumber,string GivenName,string FamilyName,DateOnly DateOfBirth,string Status);
public sealed record AdmissionDecision(string Decision);
public sealed record AdmissionReceipt(Guid RequestId,long ApplicationNumber,string Status);
public sealed record StudentCreate(Guid CampusId,Guid? ApplicationId,string GivenName,string FamilyName,DateOnly DateOfBirth);
public sealed record StudentRecord(Guid Id,Guid TenantId,Guid CampusId,string AdmissionNumber,string GivenName,string FamilyName,DateOnly DateOfBirth,string Status);
public sealed record GuardianCreate(string FullName,string Relationship,string? Phone,string? Email,bool IsPrimary);
public sealed record GuardianRecord(Guid Id,Guid StudentId,string FullName,string Relationship,string? Phone,string? Email,bool IsPrimary);
public sealed record EnrollmentCreate(Guid AcademicYearId,Guid AcademicItemId);
public sealed record EnrollmentRecord(Guid Id,Guid StudentId,Guid AcademicYearId,Guid AcademicItemId,string Status);
public sealed record LifecycleCommand(string Action,Guid? TargetCampusId,Guid? AcademicYearId,Guid? AcademicItemId,string? Reason);
public sealed record DocumentRecord(Guid Id,Guid StudentId,string Category,string FileName,string ContentType,int Length,string VerificationStatus);
public sealed record DocumentReview(string Decision);
