using EduVerse.Application.Hrms;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;
public static class HrmsEndpoints
{
 public static IEndpointRouteBuilder MapHrmsEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/hrms").RequireAuthorization();
  g.MapPost("/designations",async (HrDesignationCreate r,HrmsService s,CancellationToken ct)=>await Write(()=>s.Designate(r,ct)));
  g.MapGet("/designations",async (Guid campusId,HrmsService s,CancellationToken ct)=>await Read(()=>s.Designations(campusId,ct)));
  g.MapPost("/employees",async (HrEmployeeCreate r,HrmsService s,CancellationToken ct)=>await Write(()=>s.Hire(r,ct)));
  g.MapGet("/employees",async (Guid campusId,HrmsService s,CancellationToken ct)=>await Read(()=>s.Employees(campusId,ct)));
  g.MapPost("/employees/{employee:guid}/lifecycle",async (Guid employee,Guid campusId,HrLifecycle r,HrmsService s,CancellationToken ct)=>
   await Change(()=>s.Lifecycle(campusId,employee,r,ct)));
  g.MapGet("/employees/{employee:guid}/documents",async (Guid employee,Guid campusId,HrmsService s,CancellationToken ct)=>
   await Read(()=>s.Documents(campusId,employee,ct)));
  g.MapPost("/employees/{employee:guid}/documents",async (Guid employee,Guid campusId,string type,
    IFormFile file,HrmsService s,CancellationToken ct)=>
  {
   try
   {
    if(file.Length is < 4 or > 1048576)return Invalid("File must be 4 bytes to 1 MiB.");
    await using var memory=new MemoryStream();
    await file.CopyToAsync(memory,ct);
    return Results.Ok(await s.Upload(campusId,employee,type,file.FileName,
     file.ContentType,memory.ToArray(),ct));
   }
   catch(UnauthorizedAccessException){return Results.Forbid();}
   catch(HrScopeException){return Results.NotFound();}
   catch(ArgumentException e){return Invalid(e.Message);}
  }).DisableAntiforgery(); // Endpoint uses bearer JWT; browser BFF uses CSRF protection.
  g.MapGet("/employees/{employee:guid}/documents/{document:guid}/download",async
    (Guid employee,Guid document,Guid campusId,HrmsService s,CancellationToken ct)=>
  {
   try
   {
    var data=await s.Download(campusId,employee,document,ct);
    return Results.File(data.Bytes,"application/octet-stream",data.FileName);
   }
   catch(UnauthorizedAccessException){return Results.Forbid();}
   catch(HrScopeException){return Results.NotFound();}
  });
  g.MapPost("/leave-types",async (HrLeaveTypeCreate r,HrmsService s,CancellationToken ct)=>await Write(()=>s.LeaveType(r,ct)));
  g.MapGet("/leave-types",async (Guid campusId,HrmsService s,CancellationToken ct)=>await Read(()=>s.LeaveTypes(campusId,ct)));
  g.MapPost("/leaves",async (HrLeaveCreate r,HrmsService s,CancellationToken ct)=>await Write(()=>s.RequestLeave(r,ct)));
  g.MapGet("/leaves",async (Guid campusId,HrmsService s,CancellationToken ct)=>await Read(()=>s.Leaves(campusId,ct)));
  g.MapPost("/leaves/{leave:guid}/decision",async (Guid leave,Guid campusId,
    HrLeaveDecision r,HrmsService s,CancellationToken ct)=>await Change(()=>s.DecideLeave(campusId,leave,r.Decision,ct)));
  g.MapGet("/employees/{employee:guid}/leave-balance",async (Guid employee,
    Guid campusId,Guid leaveTypeId,int year,HrmsService s,CancellationToken ct)=>
   await Read(()=>s.Balance(campusId,employee,leaveTypeId,year,ct)));
  g.MapPost("/salaries",async (HrSalaryCreate r,HrmsService s,CancellationToken ct)=>await Write(()=>s.Salary(r,ct)));
  g.MapPost("/payroll/preview",async (Guid campusId,Guid employeeId,DateOnly month,
    HrmsService s,CancellationToken ct)=>await Write(()=>s.Preview(campusId,employeeId,month,ct)));
  g.MapGet("/attendance",async (Guid campusId,DateOnly date,HrmsService s,CancellationToken ct)=>
   await Read(()=>s.Attendance(campusId,date,ct)));
  g.MapGet("/dashboard",async (Guid campusId,HrmsService s,CancellationToken ct)=>
   await Read(()=>s.Dashboard(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> action)
 {
  try{return Results.Ok(await action());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(HrScopeException){return Results.NotFound();}
  catch(ArgumentException e){return Invalid(e.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> action)
 {
  try{return Results.Ok(await action());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(HrScopeException){return Results.NotFound();}
  catch(HrConflictException e){return Results.Conflict(new {message=e.Message});}
  catch(ArgumentException e){return Invalid(e.Message);}
  catch(MySqlException e) when(e.Number==1062){return Results.Conflict(new {message="Duplicate HRMS assignment."});}
 }
 private static async Task<IResult> Change(Func<Task> action)
 {
  try{await action();return Results.NoContent();}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(HrScopeException){return Results.NotFound();}
  catch(HrConflictException e){return Results.Conflict(new {message=e.Message});}
  catch(ArgumentException e){return Invalid(e.Message);}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["hrms"]=[message]});
}
