namespace EduVerse.Contracts;

public sealed record PeriodCreate(Guid CampusId,string Code,string Name,TimeOnly StartsAt,TimeOnly EndsAt);
public sealed record PeriodRecord(Guid Id,Guid CampusId,string Code,string Name,TimeOnly StartsAt,TimeOnly EndsAt,bool IsActive);
public sealed record TimetableCreate(Guid CampusId,Guid AcademicYearId,Guid ClassItemId,Guid SubjectId,Guid PeriodId,Guid TeacherId,int DayOfWeek);
public sealed record TimetableRecord(Guid Id,Guid CampusId,Guid AcademicYearId,Guid ClassItemId,Guid SubjectId,Guid PeriodId,Guid TeacherId,int DayOfWeek);
public sealed record StudentAttendanceMark(Guid CampusId,Guid EnrollmentId,DateOnly Date,Guid? PeriodId,string Status,string? Remarks);
public sealed record StaffAttendanceMark(Guid CampusId,Guid StaffUserId,DateOnly Date,string Status,string? Remarks);
public sealed record AttendanceRecord(Guid Id,Guid CampusId,Guid StudentId,Guid EnrollmentId,DateOnly Date,Guid? PeriodId,string Scope,string Status);
public sealed record StaffAttendanceRecord(Guid Id,Guid CampusId,Guid StaffUserId,DateOnly Date,string Status);
public sealed record AttendanceSummary(int Total,int Present,int Absent,int Late,int Excused,decimal AttendancePercent);
public sealed record AttendanceDashboard(int Periods,int TimetableSlots,int StudentMarks,int StaffMarks);
