using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;

namespace EduVerse.Application.Attendance;

/// <summary>All security, input, calendar and timetable policies are owned by the API.</summary>
public sealed class AttendanceService(IAttendanceStore store,ITenantContext context, EduVerse.Application.Identity.TenantReadAuthorizer reader)
{
 private async Task<TenantIdentity> AdminAsync(CancellationToken ct)
 {
  if(!context.TryGet(out var actor)||actor is null||!Guid.TryParse(actor.SubjectId,out _)||
     !await store.IsActiveAdminAsync(actor,ct))
    throw new UnauthorizedAccessException("Active tenant ADMIN and academic entitlement required.");
  return actor;
 }
 private static Guid Required(Guid id) => id==Guid.Empty ? throw new ArgumentException("Identifier required.") : id;
 private static (string Code,string Name) Name(string code,string name)
 {
  var c=(code??"").Trim().ToUpperInvariant();
  var n=(name??"").Trim();
  if(c.Length is < 2 or > 30||!c.All(x=>x is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-')||
     n.Length is < 2 or > 100)throw new ArgumentException("Invalid period code/name.");
  return (c,n);
 }
 private static void Date(DateOnly value)
 {
  var now=DateOnly.FromDateTime(DateTime.UtcNow);
  if(value==default||value<now.AddYears(-3)||value>now.AddYears(2))
    throw new ArgumentException("Attendance date is outside the supported recording window.");
 }
 private static void State(string status,string? remarks)
 {
  if(status is not ("PRESENT" or "ABSENT" or "LATE" or "EXCUSED")||remarks?.Length>250)
    throw new ArgumentException("Invalid attendance status or remarks.");
 }
 public async Task<PeriodRecord> CreatePeriodAsync(PeriodCreate input,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);Required(input.CampusId);
  var (code,name)=Name(input.Code,input.Name);
  if(input.StartsAt==default||input.StartsAt>=input.EndsAt)
    throw new ArgumentException("Period start must precede end.");
  return await store.CreatePeriodAsync(actor.TenantId,input with {Code=code,Name=name},ct)
   ?? throw new AttendanceScopeException();
 }
 public async Task<IReadOnlyList<PeriodRecord>> PeriodsAsync(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("attendance.read",campus,ct);
  return await store.PeriodsAsync(actor.TenantId,Required(campus),ct);
 }
 public async Task<TimetableRecord> CreateSlotAsync(TimetableCreate input,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);
  Required(input.CampusId);Required(input.AcademicYearId);Required(input.ClassItemId);
  Required(input.SubjectId);Required(input.PeriodId);Required(input.TeacherId);
  if(input.DayOfWeek is < 1 or > 7)throw new ArgumentException("DayOfWeek must be 1 (Monday) through 7 (Sunday).");
  return await store.CreateSlotAsync(actor.TenantId,input,ct)??throw new AttendanceScopeException();
 }
 public async Task<IReadOnlyList<TimetableRecord>> SlotsAsync(Guid campus,Guid year,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("attendance.read",campus,ct);
  return await store.SlotsAsync(actor.TenantId,Required(campus),Required(year),ct);
 }
 public async Task<AttendanceRecord> MarkStudentAsync(StudentAttendanceMark input,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);Required(input.CampusId);Required(input.EnrollmentId);
  Date(input.Date);State(input.Status,input.Remarks);
  if(input.PeriodId==Guid.Empty)throw new ArgumentException("Invalid period.");
  return await store.MarkStudentAsync(actor.TenantId,Guid.Parse(actor.SubjectId),input,ct)
   ??throw new AttendanceScopeException();
 }
 public async Task<IReadOnlyList<AttendanceRecord>> StudentMarksAsync(Guid campus,Guid enrollment,DateOnly? date,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);
  return await store.StudentMarksAsync(actor.TenantId,Required(campus),Required(enrollment),date,ct);
 }
 public async Task<StaffAttendanceRecord> MarkStaffAsync(StaffAttendanceMark input,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);Required(input.CampusId);Required(input.StaffUserId);
  Date(input.Date);State(input.Status,input.Remarks);
  return await store.MarkStaffAsync(actor.TenantId,Guid.Parse(actor.SubjectId),input,ct)
   ??throw new AttendanceScopeException();
 }
 public async Task<IReadOnlyList<StaffAttendanceRecord>> StaffMarksAsync(Guid campus,DateOnly date,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);Required(campus);Date(date);
  return await store.StaffMarksAsync(actor.TenantId,campus,date,ct);
 }
 public async Task<AttendanceSummary> SummaryAsync(Guid campus,Guid enrollment,CancellationToken ct)
 {
  var actor=await AdminAsync(ct);
  return await store.SummaryAsync(actor.TenantId,Required(campus),Required(enrollment),ct);
 }
 public async Task<AttendanceDashboard> DashboardAsync(Guid campus,CancellationToken ct)
 {
  var actor=await reader.RequireAsync("attendance.read",campus,ct);
  return await store.DashboardAsync(actor.TenantId,Required(campus),ct);
 }
}
