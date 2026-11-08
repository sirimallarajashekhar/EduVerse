using EduVerse.Api.Security;

namespace EduVerse.Api.Endpoints;

/// <summary>Role-specific workflow endpoints; no unrestricted learning/admin endpoints are reused.</summary>
public static class RoleLearningWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapRoleLearningWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var api=app.MapGroup("/api/v1/role-learning").RequireAuthorization();
        api.MapPost("/teacher/assignments",async (TeacherAssignmentDraft input,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Create(()=>s.DraftAssignment(input,ct)));
        api.MapGet("/teacher/submissions",async (Guid campusId,RoleLearningWorkflowStore s,CancellationToken ct)=>
            await Read(()=>s.AssignedSubmissions(campusId,ct)));
        api.MapPost("/teacher/grade",async (TeacherGrade input,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Write(()=>s.Grade(input,ct)));
        api.MapPost("/student/submit",async (StudentWork input,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Create(()=>s.Submit(input,ct)));
        api.MapGet("/student/{studentId:guid}/submissions",async (Guid studentId,Guid campusId,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Read(()=>s.MySubmissions(campusId,studentId,ct)));
        api.MapPost("/parent/conversations",async (ParentConversation input,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Create(()=>s.OpenConversation(input,ct)));
        api.MapGet("/conversations",async (Guid campusId,RoleLearningWorkflowStore s,CancellationToken ct)=>
            await Read(()=>s.Conversations(campusId,ct)));
        api.MapGet("/conversations/{id:guid}/messages",async (Guid id,Guid campusId,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Owned(()=>s.Messages(campusId,id,ct)));
        api.MapPost("/conversations/reply",async (ConversationReply input,
            RoleLearningWorkflowStore s,CancellationToken ct)=>await Create(()=>s.Reply(input,ct)));
        api.MapGet("/admin/reviews",async (Guid campusId,RoleLearningWorkflowStore s,CancellationToken ct)=>
            await Read(()=>s.Reviews(campusId,ct)));
        api.MapPost("/admin/reviews/{kind}/publish",async (string kind,LearningApproval input,
            RoleLearningWorkflowStore s,CancellationToken ct)=>
        {
            try{return await s.Publish(kind.ToUpperInvariant(),input,ct)?Results.NoContent():Results.NotFound();}
            catch(UnauthorizedAccessException){return Results.Forbid();}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        });
        return app;
    }
    private static async Task<IResult> Read<T>(Func<Task<T>> operation)
    {
        try{return Results.Ok(await operation());}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
    }
    private static async Task<IResult> Owned<T>(Func<Task<IReadOnlyList<T>?>> operation)
    {
        try
        {
            var result=await operation();
            return result is null?Results.NotFound():Results.Ok(result);
        }
        catch(UnauthorizedAccessException){return Results.Forbid();}
    }
    private static async Task<IResult> Write<T>(Func<Task<T?>> operation) where T:class
    {
        try
        {
            var result=await operation();
            return result is null?Results.NotFound():Results.Ok(result);
        }
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        catch(InvalidOperationException ex){return Results.Conflict(new {message=ex.Message});}
    }
    private static async Task<IResult> Create<T>(Func<Task<T?>> operation) where T:class
    {
        try
        {
            var result=await operation();
            return result is null?Results.NotFound():Results.Created("/api/v1/role-learning",result);
        }
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        catch(InvalidOperationException ex){return Results.Conflict(new {message=ex.Message});}
        catch(MySqlConnector.MySqlException ex) when(ex.Number==1062)
        {return Results.Conflict(new {message="Duplicate submission or message."});}
    }
}
