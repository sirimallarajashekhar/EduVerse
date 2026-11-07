using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Attendance;

public interface IAttendanceStore
{
 Task<bool> IsActiveAdminAsync(TenantIdentity actor,CancellationToken ct);
 Task<PeriodRecord?> CreatePeriodAsync(Guid tenant,PeriodCreate request,CancellationToken ct);
 Task<IReadOnlyList<PeriodRecord>> PeriodsAsync(Guid tenant,Guid campus,CancellationToken ct);
 Task<TimetableRecord?> CreateSlotAsync(Guid tenant,TimetableCreate request,CancellationToken ct);
 Task<IReadOnlyList<TimetableRecord>> SlotsAsync(Guid tenant,Guid campus,Guid year,CancellationToken ct);
 Task<AttendanceRecord?> MarkStudentAsync(Guid tenant,Guid actor,StudentAttendanceMark request,CancellationToken ct);
 Task<IReadOnlyList<AttendanceRecord>> StudentMarksAsync(Guid tenant,Guid campus,Guid enrollment,DateOnly? date,CancellationToken ct);
 Task<StaffAttendanceRecord?> MarkStaffAsync(Guid tenant,Guid actor,StaffAttendanceMark request,CancellationToken ct);
 Task<IReadOnlyList<StaffAttendanceRecord>> StaffMarksAsync(Guid tenant,Guid campus,DateOnly date,CancellationToken ct);
 Task<AttendanceSummary> SummaryAsync(Guid tenant,Guid campus,Guid enrollment,CancellationToken ct);
 Task<AttendanceDashboard> DashboardAsync(Guid tenant,Guid campus,CancellationToken ct);
}

public sealed class AttendanceScopeException:Exception { }
public sealed class TimetableConflictException:Exception
{
 public TimetableConflictException():base("Teacher, class or campus period overlaps an existing timetable allocation.") { }
}
public sealed class AttendanceHolidayException:Exception
{
 public AttendanceHolidayException():base("Attendance cannot be marked on an institutional holiday.") { }
}
