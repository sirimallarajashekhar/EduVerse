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

app.Run();

// Allows integration tests to host the application without starting a real listener.
public partial class Program { }
