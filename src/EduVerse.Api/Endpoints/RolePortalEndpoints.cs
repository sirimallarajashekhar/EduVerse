using EduVerse.Api.Security;
using EduVerse.Application.Attendance;
using EduVerse.Application.Exams;
using EduVerse.Contracts;

namespace EduVerse.Api.Endpoints;

public static class RolePortalEndpoints
{
    public static IEndpointRouteBuilder MapRolePortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group=app.MapGroup("/api/v1/portals").RequireAuthorization();
        group.MapGet("/parent/children",async (StudentOwnershipStore store,CancellationToken ct)=>
            Results.Ok((await store.MyLinksAsync(ct)).Where(x=>x.Relationship=="PARENT")));
        group.MapGet("/student/me",async (StudentOwnershipStore store,CancellationToken ct)=>
            Results.Ok((await store.MyLinksAsync(ct)).Where(x=>x.Relationship=="SELF")));
        group.MapGet("/teacher/classes",async (Guid campusId,StudentOwnershipStore store,CancellationToken ct)=>
            await Read(()=>store.ClassesAsync(campusId,ct)));
        group.MapGet("/teacher/classes/{classId:guid}/students",async (Guid classId,Guid campusId,
            RolePortalStore store,CancellationToken ct)=>
            await Read(()=>store.TeachingRoster(campusId,classId,ct)));
        group.MapGet("/teacher/lessons",async (Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Read(()=>store.TeachingLessons(campusId,ct)));

        group.MapPost("/teacher/attendance",async (StudentAttendanceMark request,AttendanceService service,CancellationToken ct)=>
        {
            try {return Results.Ok(await service.MarkStudentAsync(request,ct));}
            catch(UnauthorizedAccessException){return Results.Forbid();}
            catch(AttendanceScopeException){return Results.NotFound();}
            catch(AttendanceHolidayException){return Results.Conflict(new {message="Academic holiday"});}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        });
        group.MapPost("/teacher/marks",async (MarkCreate request,ExamService service,CancellationToken ct)=>
        {
            try {return Results.Ok(await service.EnterMark(request,ct));}
            catch(UnauthorizedAccessException){return Results.Forbid();}
            catch(ExamScopeException){return Results.NotFound();}
            catch(ArgumentException ex){return Results.BadRequest(new {message=ex.Message});}
        });

        group.MapGet("/parent/children/{studentId:guid}/attendance",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Attendance(campusId,studentId,"PARENT",ct)));
        group.MapGet("/parent/children/{studentId:guid}/results",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Results(campusId,studentId,"PARENT",ct)));
        group.MapGet("/parent/children/{studentId:guid}/notices",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Notices(campusId,studentId,"PARENT",ct)));
        group.MapGet("/student/{studentId:guid}/attendance",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Attendance(campusId,studentId,"SELF",ct)));
        group.MapGet("/student/{studentId:guid}/results",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Results(campusId,studentId,"SELF",ct)));
        group.MapGet("/student/{studentId:guid}/timetable",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Timetable(campusId,studentId,ct)));
        group.MapGet("/student/{studentId:guid}/assignments",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Assignments(campusId,studentId,ct)));
        group.MapGet("/student/{studentId:guid}/notices",async (
            Guid studentId,Guid campusId,RolePortalStore store,CancellationToken ct)=>
            await Owned(()=>store.Notices(campusId,studentId,"SELF",ct)));
        return app;
    }

    private static async Task<IResult> Read<T>(Func<Task<T>> task)
    {
        try{return Results.Ok(await task());}
        catch(UnauthorizedAccessException){return Results.Forbid();}
    }
    private static async Task<IResult> Owned<T>(Func<Task<IReadOnlyList<T>?>> task)
    {
        try
        {
            var result=await task();
            return result is null?Results.NotFound():Results.Ok(result);
        }
        catch(UnauthorizedAccessException){return Results.Forbid();}
    }
}
