using EduVerse.Application.Attendance;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

public static class AttendanceEndpoints
{
 public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder app)
 {
  var group=app.MapGroup("/api/v1/attendance").RequireAuthorization();
  group.MapPost("/periods",async (PeriodCreate request,AttendanceService service,CancellationToken ct)=>
   await Write(()=>service.CreatePeriodAsync(request,ct)));
  group.MapGet("/periods",async (Guid campusId,AttendanceService service,CancellationToken ct)=>
   await Read(()=>service.PeriodsAsync(campusId,ct)));
  group.MapPost("/timetable",async (TimetableCreate request,AttendanceService service,CancellationToken ct)=>
   await Write(()=>service.CreateSlotAsync(request,ct)));
  group.MapGet("/timetable",async (Guid campusId,Guid academicYearId,AttendanceService service,CancellationToken ct)=>
   await Read(()=>service.SlotsAsync(campusId,academicYearId,ct)));
  group.MapPost("/students",async (StudentAttendanceMark request,AttendanceService service,CancellationToken ct)=>
   await Write(()=>service.MarkStudentAsync(request,ct)));
  group.MapGet("/students",async (Guid campusId,Guid enrollmentId,DateOnly? date,AttendanceService service,CancellationToken ct)=>
   await Read(()=>service.StudentMarksAsync(campusId,enrollmentId,date,ct)));
  group.MapGet("/students/summary",async (Guid campusId,Guid enrollmentId,AttendanceService service,CancellationToken ct)=>
   await Read(()=>service.SummaryAsync(campusId,enrollmentId,ct)));
  group.MapPost("/staff",async (StaffAttendanceMark request,AttendanceService service,CancellationToken ct)=>
   await Write(()=>service.MarkStaffAsync(request,ct)));
  group.MapGet("/staff",async (Guid campusId,DateOnly date,AttendanceService service,CancellationToken ct)=>
   await Read(()=>service.StaffMarksAsync(campusId,date,ct)));
  group.MapGet("/dashboard",async (Guid campusId,AttendanceService service,CancellationToken ct)=>
   await Read(()=>service.DashboardAsync(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(ArgumentException ex){return Invalid(ex.Message);}
  catch(AttendanceScopeException){return Results.NotFound();}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(ArgumentException ex){return Invalid(ex.Message);}
  catch(AttendanceScopeException){return Results.NotFound();}
  catch(AttendanceHolidayException){return Results.Conflict(new {message="Attendance is blocked on an academic holiday."});}
  catch(TimetableConflictException){return Results.Conflict(new {message="Overlapping timetable allocation."});}
  catch(MySqlException ex) when(ex.Number==1062)
  {return Results.Conflict(new {message="Period code or timetable allocation already exists."});}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["attendance"]=[message]});
}
