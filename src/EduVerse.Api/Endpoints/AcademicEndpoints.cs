using EduVerse.Api.Security;
using EduVerse.Application.Academic;
using EduVerse.Contracts;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

/// <summary>Versioned academic API. All authorization, relationship validation, tenant/campus filtering live in backend.</summary>
public static class AcademicEndpoints
{
    public static IEndpointRouteBuilder MapAcademicEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/academic").RequireAuthorization().RequireModuleActions("academic.read","academic.manage", "/api/v1/academic/years", "/api/v1/academic/catalog", "/api/v1/academic/faculty-assignments", "/api/v1/academic/calendar", "/api/v1/academic/overview");
        group.MapGet("/years",async (Guid campusId,AcademicService service,CancellationToken ct)=>
            await ReadAsync(()=>service.YearsAsync(campusId,ct)));
        group.MapPost("/years",async (AcademicYearCreate request,AcademicService service,CancellationToken ct)=>
            await WriteAsync(()=>service.CreateYearAsync(request,ct)));
        group.MapGet("/catalog",async (Guid campusId,string? kind,AcademicService service,CancellationToken ct)=>
            await ReadAsync(()=>service.ItemsAsync(campusId,kind,ct)));
        group.MapPost("/catalog",async (AcademicItemCreate request,AcademicService service,CancellationToken ct)=>
            await WriteAsync(()=>service.CreateItemAsync(request,ct)));
        group.MapGet("/faculty-assignments",async (Guid campusId,AcademicService service,CancellationToken ct)=>
            await ReadAsync(()=>service.FacultyAsync(campusId,ct)));
        group.MapPost("/faculty-assignments",async (FacultyAssignmentCreate request,AcademicService service,CancellationToken ct)=>
            await WriteAsync(()=>service.AssignFacultyAsync(request,ct)));
        group.MapGet("/calendar",async (Guid campusId,AcademicService service,CancellationToken ct)=>
            await ReadAsync(()=>service.EventsAsync(campusId,ct)));
        group.MapPost("/calendar",async (AcademicEventCreate request,AcademicService service,CancellationToken ct)=>
            await WriteAsync(()=>service.CreateEventAsync(request,ct)));
        group.MapGet("/overview",async (Guid campusId,AcademicService service,CancellationToken ct)=>
            await ReadAsync(()=>service.OverviewAsync(campusId,ct)));
        return app;
    }

    private static async Task<IResult> ReadAsync<T>(Func<Task<T>> operation)
    {
        try {return Results.Ok(await operation());}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(ArgumentException ex){return Invalid(ex.Message);}
    }
    private static async Task<IResult> WriteAsync<T>(Func<Task<T>> operation)
    {
        try {return Results.Created("/api/v1/academic",await operation());}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(AcademicScopeException){return Results.NotFound();}
        catch(ArgumentException ex){return Invalid(ex.Message);}
        catch(MySqlException ex) when(ex.Number==1062)
        {return Results.Conflict(new {message="Academic code or assignment already exists in this campus."});}
    }
    private static IResult Invalid(string message) =>
        Results.ValidationProblem(new Dictionary<string,string[]> { ["academic"]=[message] });
}
