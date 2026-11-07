namespace EduVerse.Contracts;

public sealed record AcademicYearCreate(Guid CampusId, string Code, string Name, DateOnly StartsOn, DateOnly EndsOn);
public sealed record AcademicYear(Guid Id, Guid TenantId, Guid CampusId, string Code, string Name, DateOnly StartsOn, DateOnly EndsOn, bool IsActive);

/// <summary>Kind values: TERM, SEMESTER, GRADE, CLASS, SECTION, PROGRAM, COURSE, BATCH, SUBJECT.</summary>
public sealed record AcademicItemCreate(Guid CampusId, string Kind, string Code, string Name,
    Guid? ParentId, Guid? AcademicYearId, Guid? DepartmentId, DateOnly? StartsOn, DateOnly? EndsOn);
public sealed record AcademicItem(Guid Id, Guid TenantId, Guid CampusId, string Kind, string Code, string Name,
    Guid? ParentId, Guid? AcademicYearId, Guid? DepartmentId, DateOnly? StartsOn, DateOnly? EndsOn, bool IsActive);

public sealed record FacultyAssignmentCreate(Guid CampusId, Guid SubjectId, Guid AcademicYearId, Guid FacultyUserId);
public sealed record FacultyAssignment(Guid Id, Guid TenantId, Guid CampusId, Guid SubjectId, Guid AcademicYearId, Guid FacultyUserId, bool IsActive);

public sealed record AcademicEventCreate(Guid CampusId, Guid AcademicYearId, string Kind, string Title,
    DateOnly StartsOn, DateOnly EndsOn, string? Notes);
public sealed record AcademicEvent(Guid Id, Guid TenantId, Guid CampusId, Guid AcademicYearId, string Kind,
    string Title, DateOnly StartsOn, DateOnly EndsOn, string? Notes);

public sealed record AcademicOverview(int Years, int Programs, int Grades, int Classes, int Sections,
    int Courses, int Subjects, int Batches, int Assignments, int CalendarEvents);
