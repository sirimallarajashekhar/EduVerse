using EduVerse.Api.Endpoints;
using EduVerse.Api.Modules;
using EduVerse.Api.Security;
using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Infrastructure.MySql;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();

// Production tenant endpoints only accept validated authenticated principals.
// Phase 4 introduces the central identity issuer and token provisioning.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = true;
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<InstitutionService>();
builder.Services.AddScoped<IInstitutionRepository>(provider =>
{
    var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("EduVerse")
        ?? throw new InvalidOperationException("ConnectionStrings:EduVerse is required for tenant database operations.");
    return new MySqlInstitutionRepository(connectionString);
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api/v1");
api.MapGet("/system/info", () => TypedResults.Ok(
    new SystemInfoResponse("EduVerse", "v1", DateTimeOffset.UtcNow)))
    .WithName("GetSystemInfo");
api.MapGet("/platform/modules", () => TypedResults.Ok(ModuleRegistry.All))
    .WithName("ListPlatformModules");
app.MapInstitutionEndpoints();
app.Run();

public partial class Program { }
