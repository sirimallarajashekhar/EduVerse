using EduVerse.Application.Exams;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;
public static class ExamEndpoints
{
 public static IEndpointRouteBuilder MapExamEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/exams").RequireAuthorization();
  g.MapPost("/schemes",async (GradingSchemeCreate r,ExamService s,CancellationToken ct)=>
   await Write(()=>s.CreateScheme(r,ct)));
  g.MapGet("/schemes",async (Guid campusId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Schemes(campusId,ct)));
  g.MapPost("",async (ExamCreate r,ExamService s,CancellationToken ct)=>
   await Write(()=>s.CreateExam(r,ct)));
  g.MapGet("",async (Guid campusId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Exams(campusId,ct)));
  g.MapPost("/assessments",async (AssessmentCreate r,ExamService s,CancellationToken ct)=>
   await Write(()=>s.AddAssessment(r,ct)));
  g.MapGet("/assessments",async (Guid campusId,Guid examId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Assessments(campusId,examId,ct)));
  g.MapPost("/marks",async (MarkCreate r,ExamService s,CancellationToken ct)=>
   await Write(()=>s.EnterMark(r,ct)));
  g.MapGet("/marks",async (Guid campusId,Guid assessmentId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Marks(campusId,assessmentId,ct)));
  g.MapPost("/{id:guid}/approve",async (Guid id,Guid campusId,ExamService s,CancellationToken ct)=>
   await Change(()=>s.Transition(campusId,id,"APPROVE",ct)));
  g.MapPost("/{id:guid}/publish",async (Guid id,Guid campusId,ExamService s,CancellationToken ct)=>
   await Change(()=>s.Transition(campusId,id,"PUBLISH",ct)));
  g.MapGet("/{id:guid}/results/{enrollment:guid}",async (Guid id,Guid enrollment,
   Guid campusId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Result(campusId,id,enrollment,ct)));
  g.MapGet("/{id:guid}/analytics",async (Guid id,Guid campusId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Analytics(campusId,id,ct)));
  g.MapGet("/students/{id:guid}/cgpa",async (Guid id,Guid campusId,ExamService s,CancellationToken ct)=>
   await Read(()=>s.Cgpa(campusId,id,ct)));
  g.MapGet("/{id:guid}/results/{enrollment:guid}/download",async (Guid id,Guid enrollment,
   Guid campusId,ExamService s,CancellationToken ct)=>
  {
   try
   {
    var result=await s.Result(campusId,id,enrollment,ct);
    var text=string.Join("\n",new[]{
     "EduVerse | Academic Report Card",
     "Student: "+result.Student,
     "Admission Number: "+result.AdmissionNumber,
     "Exam: "+result.Exam,
     "Total: "+result.Total.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)
      +" / "+result.Maximum.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
     "Percentage: "+result.Percentage.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+"%",
     "GPA: "+result.Gpa.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),
     "Result: "+(result.Passed?"PASS":"FAIL")
    }.Concat(result.Subjects.Select(x=>x.Subject+": "+(x.Absent?"ABSENT":x.Marks?.ToString("F2")??"-")
      +"/"+x.MaxMarks.ToString("F2")+" ("+x.Grade+")")));
    return Results.File(System.Text.Encoding.UTF8.GetBytes(text),"text/plain; charset=utf-8",
     fileDownloadName:"eduverse-report-"+id.ToString("N")+".txt");
   }
   catch(UnauthorizedAccessException){return Results.Forbid();}
   catch(ExamScopeException){return Results.NotFound();}
  });
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(ExamScopeException){return Results.NotFound();}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> work)
 {
  try{return Results.Ok(await work());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(ExamScopeException){return Results.NotFound();}
  catch(ExamConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
  catch(MySqlException ex) when(ex.Number==1062){return Results.Conflict(new {message="Duplicate exam, grade or marks entry."});}
 }
 private static async Task<IResult> Change(Func<Task> work)
 {
  try{await work();return Results.NoContent();}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(ExamScopeException){return Results.NotFound();}
  catch(ExamConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["exams"]=[message]});
}
