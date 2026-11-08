using EduVerse.Application.Identity;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

/// <summary>Institution ADMIN only; tenant context and authorization are checked by application services.</summary>
public static class AdvancedRbacEndpoints
{
    public static IEndpointRouteBuilder MapAdvancedRbacEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/rbac").RequireAuthorization();
        group.MapGet("/catalog",() => Results.Ok(KnownPermissions.All.OrderBy(x=>x).ToArray()));

        group.MapGet("/roles",async (AdvancedRbacService service,CancellationToken ct)=>
            await Read(()=>service.Roles(ct)));
        group.MapGet("/assignments",async (AdvancedRbacService service,CancellationToken ct)=>
            await Read(()=>service.Assignments(ct)));
        group.MapPost("/roles",async (CreateRbacRole role,AdvancedRbacService service,CancellationToken ct)=>
            await Change(()=>service.Create(role,ct)));
        group.MapPut("/roles/{code}/permissions",async (string code,SetRbacPermissions request,
            AdvancedRbacService service,CancellationToken ct)=>
            await Change(()=>service.SetPermissions(code,request,ct)));
        group.MapPost("/assignments",async (SetRbacAssignment request,AdvancedRbacService service,CancellationToken ct)=>
            await UpdateAssignment(service,request,true,ct));
        group.MapDelete("/assignments/{userId:guid}/{roleCode}",async (Guid userId,string roleCode,
            AdvancedRbacService service,CancellationToken ct)=>
            await UpdateAssignment(service,new SetRbacAssignment(userId.ToString("D"),roleCode),false,ct));
        return app;
    }

    private static async Task<IResult> Read<T>(Func<Task<T>> operation)
    {
        try {return Results.Ok(await operation());}
        catch(UnauthorizedAccessException) {return Results.Forbid();}
    }

    private static async Task<IResult> Change(Func<Task> operation)
    {
        try {await operation();return Results.NoContent();}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        catch(KeyNotFoundException){return Results.NotFound();}
        catch(MySqlException ex) when(ex.Number==1062){return Results.Conflict(new {message="Role already exists."});}
    }

    private static async Task<IResult> UpdateAssignment(AdvancedRbacService service,
        SetRbacAssignment request,bool assign,CancellationToken ct)
    {
        try
        {
            return await service.Assign(request,assign,ct)
                ? Results.NoContent() : Results.NotFound();
        }
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        catch(KeyNotFoundException){return Results.NotFound();}
    }
}
