using EduVerse.Api.Security;

namespace EduVerse.Api.Endpoints;

/// <summary>Dedicated self-service reads: never reuse unrestricted administration endpoints.</summary>
public static class OwnershipEndpoints
{
    public static IEndpointRouteBuilder MapOwnershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/ownership").RequireAuthorization();
        group.MapPost("/links",async (StudentPortalLinkRequest request,
            StudentOwnershipStore store,CancellationToken ct)=>
        {
            try {return await store.LinkAsync(request,ct)?Results.NoContent():Results.NotFound();}
            catch(UnauthorizedAccessException){return Results.Forbid();}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        });

        group.MapGet("/my/students",async (StudentOwnershipStore store,CancellationToken ct)=>
        {
            try{return Results.Ok(await store.MyLinksAsync(ct));}
            catch(UnauthorizedAccessException){return Results.Forbid();}
        });
        group.MapGet("/my/students/{studentId:guid}",async (Guid studentId,Guid campusId,
            StudentOwnershipStore store,CancellationToken ct)=>
        {
            try {var student=await store.MineAsync(campusId,studentId,ct);
                 return student is null?Results.NotFound():Results.Ok(student);}
            catch(UnauthorizedAccessException){return Results.Forbid();}
        });
        group.MapGet("/my/teaching/classes",async (Guid campusId,StudentOwnershipStore store,CancellationToken ct)=>
        {
            try{return Results.Ok(await store.ClassesAsync(campusId,ct));}
            catch(UnauthorizedAccessException){return Results.Forbid();}
        });
        group.MapGet("/my/teaching/classes/{classId:guid}/students",async (
            Guid classId,Guid campusId,StudentOwnershipStore store,CancellationToken ct)=>
        {
            try{return Results.Ok(await store.ClassStudentsAsync(campusId,classId,ct));}
            catch(UnauthorizedAccessException){return Results.Forbid();}
        });
        return app;
    }
}
