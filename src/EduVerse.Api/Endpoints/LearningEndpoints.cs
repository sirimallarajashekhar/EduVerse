using EduVerse.Application.Learning;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;
public static class LearningEndpoints
{
 public static IEndpointRouteBuilder MapLearningEndpoints(this IEndpointRouteBuilder app)
 {
  var g=app.MapGroup("/api/v1/learning").RequireAuthorization();
  g.MapPost("/lessons",async (LessonCreate r,LearningService s,CancellationToken ct)=>await Write(()=>s.CreateLesson(r,ct)));
  g.MapGet("/lessons",async (Guid campusId,LearningService s,CancellationToken ct)=>await Read(()=>s.Lessons(campusId,ct)));
  g.MapPost("/lessons/{id:guid}/publish",async (Guid id,Guid campusId,LearningService s,CancellationToken ct)=>
   await Change(()=>s.PublishLesson(campusId,id,ct)));
  g.MapPost("/assignments",async (AssignmentCreate r,LearningService s,CancellationToken ct)=>await Write(()=>s.CreateAssignment(r,ct)));
  g.MapGet("/assignments",async (Guid campusId,LearningService s,CancellationToken ct)=>await Read(()=>s.Assignments(campusId,ct)));
  g.MapPost("/assignments/{id:guid}/publish",async (Guid id,Guid campusId,LearningService s,CancellationToken ct)=>
   await Change(()=>s.PublishAssignment(campusId,id,ct)));
  g.MapPost("/submissions",async (SubmissionCreate r,LearningService s,CancellationToken ct)=>await Write(()=>s.Submit(r,ct)));
  g.MapGet("/submissions",async (Guid campusId,LearningService s,CancellationToken ct)=>await Read(()=>s.Submissions(campusId,ct)));
  g.MapPost("/submissions/{id:guid}/grade",async (Guid id,Guid campusId,SubmissionGrade r,LearningService s,CancellationToken ct)=>
   await Write(()=>s.Grade(campusId,id,r,ct)));
  g.MapGet("/enrollments/{id:guid}/progress",async (Guid id,Guid campusId,LearningService s,CancellationToken ct)=>
   await Read(()=>s.Progress(campusId,id,ct)));
  g.MapPost("/announcements",async (AnnouncementCreate r,LearningService s,CancellationToken ct)=>await Write(()=>s.Announce(r,ct)));
  g.MapGet("/announcements",async (Guid campusId,LearningService s,CancellationToken ct)=>await Read(()=>s.Announcements(campusId,ct)));
  g.MapPost("/threads",async (ParentThreadCreate r,LearningService s,CancellationToken ct)=>await Write(()=>s.OpenThread(r,ct)));
  g.MapGet("/threads",async (Guid campusId,LearningService s,CancellationToken ct)=>await Read(()=>s.Threads(campusId,ct)));
  g.MapPost("/messages",async (ParentMessageCreate r,LearningService s,CancellationToken ct)=>await Write(()=>s.SendMessage(r,ct)));
  g.MapGet("/threads/{id:guid}/messages",async (Guid id,Guid campusId,LearningService s,CancellationToken ct)=>
   await Read(()=>s.Messages(campusId,id,ct)));
  g.MapGet("/notifications",async (Guid campusId,LearningService s,CancellationToken ct)=>await Read(()=>s.Notifications(campusId,ct)));
  g.MapPost("/notifications/{id:guid}/read",async (Guid id,Guid campusId,LearningService s,CancellationToken ct)=>
   await Change(()=>s.MarkRead(campusId,id,ct)));
  g.MapGet("/dashboard",async (Guid campusId,LearningService s,CancellationToken ct)=>
   await Read(()=>s.Dashboard(campusId,ct)));
  return app;
 }
 private static async Task<IResult> Read<T>(Func<Task<T>> f)
 {
  try{return Results.Ok(await f());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(LearningScopeException){return Results.NotFound();}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static async Task<IResult> Write<T>(Func<Task<T>> f)
 {
  try{return Results.Ok(await f());}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(LearningScopeException){return Results.NotFound();}
  catch(LearningConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
  catch(MySqlException ex) when(ex.Number==1062){return Results.Conflict(new {message="Duplicate submission or other unique item."});}
 }
 private static async Task<IResult> Change(Func<Task> f)
 {
  try{await f();return Results.NoContent();}
  catch(UnauthorizedAccessException){return Results.Forbid();}
  catch(LearningScopeException){return Results.NotFound();}
  catch(LearningConflictException ex){return Results.Conflict(new {message=ex.Message});}
  catch(ArgumentException ex){return Invalid(ex.Message);}
 }
 private static IResult Invalid(string message)=>Results.ValidationProblem(
  new Dictionary<string,string[]> {["learning"]=[message]});
}
