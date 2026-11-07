using EduVerse.Application.Students;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Http.Features;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

/// <summary>Private student records: all routes require validated JWT and live tenant ADMIN checks.</summary>
public static class StudentEndpoints
{
    public static IEndpointRouteBuilder MapStudentEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/students").RequireAuthorization();
        group.MapPost("/applications",async (AdmissionSubmit request,StudentService service,CancellationToken ct)=>
            await Write(()=>service.SubmitAsync(request,ct)));
        group.MapGet("/applications",async (Guid campusId,StudentService service,CancellationToken ct)=>
            await Read(()=>service.ApplicationsAsync(campusId,ct)));
        group.MapPost("/applications/{id:guid}/decision",async (Guid id,Guid campusId,AdmissionDecision request,StudentService service,CancellationToken ct)=>
            await Change(()=>service.DecideAsync(campusId,id,request.Decision,ct)));
        group.MapPost("",async (StudentCreate request,StudentService service,CancellationToken ct)=>
            await Write(()=>service.CreateStudentAsync(request,ct)));
        group.MapGet("",async (Guid campusId,StudentService service,CancellationToken ct)=>
            await Read(()=>service.StudentsAsync(campusId,ct)));
        group.MapGet("/{id:guid}",async (Guid id,Guid campusId,StudentService service,CancellationToken ct)=>
            await Read(()=>service.StudentAsync(campusId,id,ct)));
        group.MapPost("/{id:guid}/guardians",async (Guid id,Guid campusId,GuardianCreate request,StudentService service,CancellationToken ct)=>
            await Write(()=>service.AddGuardianAsync(campusId,id,request,ct)));
        group.MapGet("/{id:guid}/guardians",async (Guid id,Guid campusId,StudentService service,CancellationToken ct)=>
            await Read(()=>service.GuardiansAsync(campusId,id,ct)));
        group.MapPost("/{id:guid}/enrollments",async (Guid id,Guid campusId,EnrollmentCreate request,StudentService service,CancellationToken ct)=>
            await Write(()=>service.EnrollAsync(campusId,id,request,ct)));
        group.MapGet("/{id:guid}/enrollments",async (Guid id,Guid campusId,StudentService service,CancellationToken ct)=>
            await Read(()=>service.EnrollmentsAsync(campusId,id,ct)));
        group.MapPost("/{id:guid}/lifecycle",async (Guid id,Guid campusId,LifecycleCommand request,StudentService service,CancellationToken ct)=>
            await Change(()=>service.LifecycleAsync(campusId,id,request,ct)));

        group.MapPost("/{id:guid}/documents",async (Guid id,Guid campusId,IFormFile file,
            string category,StudentService service,CancellationToken ct)=>
        {
            try
            {
                if(file.Length is < 4 or > 1048576)
                    return Invalid("File must be 4 bytes to 1 MiB.");
                await using var memory=new MemoryStream();
                await file.CopyToAsync(memory,ct);
                return Results.Created("/api/v1/students/"+id+"/documents",
                    await service.UploadAsync(campusId,id,category,file.FileName,file.ContentType,memory.ToArray(),ct));
            }
            catch(UnauthorizedAccessException){return Results.Forbid();}
            catch(StudentScopeException){return Results.NotFound();}
            catch(ArgumentException ex){return Invalid(ex.Message);}
        }).DisableAntiforgery(); // Bearer JWT, not browser cookie. BFF validates CSRF for its own multipart route.
        group.MapGet("/{id:guid}/documents",async (Guid id,Guid campusId,StudentService service,CancellationToken ct)=>
            await Read(()=>service.DocumentsAsync(campusId,id,ct)));
        group.MapGet("/{id:guid}/documents/{document:guid}",async (Guid id,Guid document,
            Guid campusId,StudentService service,CancellationToken ct)=>
        {
            try
            {
                var file=await service.ReadDocumentAsync(campusId,id,document,ct);
                return Results.File(file.Bytes,"application/octet-stream",file.Name,enableRangeProcessing:false);
            }
            catch(UnauthorizedAccessException){return Results.Forbid();}
            catch(StudentScopeException){return Results.NotFound();}
        });
        group.MapPost("/{id:guid}/documents/{document:guid}/review",async (Guid id,
            Guid document,Guid campusId,DocumentReview request,StudentService service,CancellationToken ct)=>
            await Change(()=>service.ReviewDocumentAsync(campusId,id,document,request.Decision,ct)));
        return app;
    }
    private static async Task<IResult> Read<T>(Func<Task<T>> work)
    {
        try{return Results.Ok(await work());}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(StudentScopeException){return Results.NotFound();}
        catch(ArgumentException ex){return Invalid(ex.Message);}
    }
    private static async Task<IResult> Write<T>(Func<Task<T>> work)
    {
        try{return Results.Created("/api/v1/students",await work());}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(StudentScopeException){return Results.NotFound();}
        catch(ArgumentException ex){return Invalid(ex.Message);}
        catch(MySqlException ex) when(ex.Number==1062)
        {return Results.Conflict(new { message="Student record or enrollment already exists." });}
    }
    private static async Task<IResult> Change(Func<Task> work)
    {
        try{await work();return Results.NoContent();}
        catch(UnauthorizedAccessException){return Results.Forbid();}
        catch(StudentScopeException){return Results.NotFound();}
        catch(ArgumentException ex){return Invalid(ex.Message);}
        catch(MySqlException ex) when(ex.Number==1062)
        {return Results.Conflict(new {message="Enrollment for this academic year already exists."});}
    }
    private static IResult Invalid(string message)=>Results.ValidationProblem(
        new Dictionary<string,string[]> {["students"]=[message]});
}
