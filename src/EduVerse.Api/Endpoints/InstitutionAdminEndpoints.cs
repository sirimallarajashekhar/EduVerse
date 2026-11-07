using EduVerse.Application.Administration;
using EduVerse.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;

namespace EduVerse.Api.Endpoints;

public static class InstitutionAdminEndpoints
{
    public static IEndpointRouteBuilder MapInstitutionAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // Public registration stores only a PENDING request. No anonymous tenant/admin creation.
        app.MapPost("/api/v1/institution-registration/requests",
            async (RegistrationRequest request, InstitutionAdminService service, CancellationToken ct) =>
            {
                try
                {
                    var id=await service.RequestRegistrationAsync(request,ct);
                    return Results.Accepted(value:new RegistrationReceipt(id,"PENDING"));
                }
                catch(ArgumentException ex)
                {
                    return Results.ValidationProblem(new Dictionary<string,string[]>
                    {["registration"]=[ex.Message]});
                }
            }).RequireRateLimiting("auth");

        var group=app.MapGroup("/api/v1/institution-admin").RequireAuthorization();

        group.MapGet("/dashboard",async (InstitutionAdminService service,CancellationToken ct) =>
            await InvokeAsync(() => service.DashboardAsync(ct)));

        group.MapGet("/profile",async (InstitutionAdminService service,CancellationToken ct) =>
            await InvokeAsync(() => service.ProfileAsync(ct)));

        group.MapPut("/profile",async (ProfileUpdate request,InstitutionAdminService service,CancellationToken ct) =>
            await ChangeAsync(async () => { await service.SaveProfileAsync(request,ct); }));

        group.MapPost("/onboarding/ready",async (InstitutionAdminService service,CancellationToken ct) =>
            await ChangeAsync(async () => { await service.MarkReadyAsync(ct); }));

        group.MapGet("/resources/{type}",async (string type,Guid? campusId,
            InstitutionAdminService service,CancellationToken ct) =>
            await InvokeAsync(() => service.ResourcesAsync(type,campusId,ct)));

        group.MapPost("/resources/{type}",async (string type,ResourceCreate request,
            InstitutionAdminService service,CancellationToken ct) =>
        {
            try { return Results.Created("/api/v1/institution-admin/resources/"+type,
                        await service.CreateResourceAsync(type,request,ct)); }
            catch(InvalidCampusScopeException) { return Results.NotFound(); }
            catch(UnauthorizedAccessException) { return Results.Forbid(); }
            catch(ArgumentException ex) { return Validation(ex.Message); }
            catch(MySqlException ex) when(ex.Number==1062)
            { return Results.Conflict(new { message="Resource code already exists for this campus." }); }
        });

        group.MapGet("/settings",async (InstitutionAdminService service,CancellationToken ct) =>
            await InvokeAsync(() => service.SettingsAsync(ct)));

        group.MapPut("/settings",async (InstitutionSetting setting,
            InstitutionAdminService service,CancellationToken ct) =>
            await ChangeAsync(async () => {await service.SaveSettingAsync(setting,ct);}));

        group.MapGet("/plans",async (InstitutionAdminService service,CancellationToken ct) =>
            await InvokeAsync(() => service.PlansAsync(ct)));

        group.MapGet("/subscription",async (InstitutionAdminService service,CancellationToken ct) =>
            await InvokeAsync(() => service.SubscriptionAsync(ct)));

        return app;
    }

    private static async Task<IResult> InvokeAsync<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch(UnauthorizedAccessException) { return Results.Forbid(); }
        catch(ArgumentException ex) { return Validation(ex.Message); }
    }
    private static async Task<IResult> ChangeAsync(Func<Task> action)
    {
        try { await action(); return Results.NoContent(); }
        catch(UnauthorizedAccessException) { return Results.Forbid(); }
        catch(ArgumentException ex) { return Validation(ex.Message); }
    }
    private static IResult Validation(string message) => Results.ValidationProblem(
        new Dictionary<string,string[]> {["institution"]=[message]});
}
