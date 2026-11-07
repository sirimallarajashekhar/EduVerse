using EduVerse.Application.College;
using EduVerse.Contracts;
using MySqlConnector;
namespace EduVerse.Api.Endpoints;
public static class CollegeEndpoints
{
 public static IEndpointRouteBuilder MapCollegeEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/college").RequireAuthorization();
  g.MapPost("/regulations",async (CollegeRegulationCreate r,CollegeService s,CancellationToken ct)=>await Write(()=>s.CreateRegulation(r,ct)));
  g.MapGet("/regulations",async (Guid campusId,CollegeService s,CancellationToken ct)=>await Read(()=>s.Regulations(campusId,ct)));
  g.MapPost("/programs",async (CollegeProgramCreate r,CollegeService s,CancellationToken ct)=>await Write(()=>s.CreateProgram(r,ct)));
  g.MapGet("/programs",async (Guid campusId,CollegeService s,CancellationToken ct)=>await Read(()=>s.Programs(campusId,ct)));
  g.MapPost("/semesters",async (CollegeSemesterCreate r,CollegeService s,CancellationToken ct)=>await Write(()=>s.CreateSemester(r,ct)));
  g.MapGet("/semesters",async (Guid campusId,CollegeService s,CancellationToken ct)=>await Read(()=>s.Semesters(campusId,ct)));
  g.MapPost("/courses",async (CollegeCourseCreate r,CollegeService s,CancellationToken ct)=>await Write(()=>s.CreateCourse(r,ct)));
  g.MapGet("/courses",async (Guid campusId,CollegeService s,CancellationToken ct)=>await Read(()=>s.Courses(campusId,ct)));
  g.MapPost("/registrations",async (CollegeRegistrationCreate r,CollegeService s,CancellationToken ct)=>await Write(()=>s.Register(r,ct)));
  g.MapGet("/registrations",async (Guid campusId,Guid enrollmentId,CollegeService s,CancellationToken ct)=>
   await Read(()=>s.Registrations(campusId,enrollmentId,ct)));
  g.MapPost("/grades/import",async (CollegeGradeFromExam r,CollegeService s,CancellationToken ct)=>
   await Write(()=>s.ImportGrade(r,ct)));
  g.MapPost("/semesters/finalize",async (CollegeFinalize r,CollegeService s,CancellationToken ct)=>
   await Write(()=>s.Finalize(r,ct)));
  g.MapGet("/transcripts",async (Guid campusId,Guid enrollmentId,CollegeService s,CancellationToken ct)=>
   await Read(()=>s.Transcript(campusId,enrollmentId,ct)));
  g.MapGet("/report",async (Guid campusId,CollegeService s,CancellationToken ct)=>
   await Read(()=>s.Report(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(CollegeScopeException){return Results.NotFound();}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(CollegeScopeException){return Results.NotFound();}
  catch(CollegeConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
  catch(MySqlException ex) when(ex.Number==1062){return Results.Conflict(new {message="Duplicate college code, capacity assignment or finalized result."});}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(new Dictionary<string,string[]> {["college"]=[message]});
}
