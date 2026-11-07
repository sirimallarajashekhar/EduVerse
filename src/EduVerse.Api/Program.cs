using EduVerse.Api.Modules;
using EduVerse.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api/v1");
api.MapGet("/system/info", () => TypedResults.Ok(
    new SystemInfoResponse("EduVerse", "v1", DateTimeOffset.UtcNow)))
    .WithName("GetSystemInfo");
api.MapGet("/platform/modules", () => TypedResults.Ok(ModuleRegistry.All))
    .WithName("ListPlatformModules");

app.Run();

public partial class Program { }
