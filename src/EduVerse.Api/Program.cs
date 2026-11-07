using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using EduVerse.Api.Endpoints;
using EduVerse.Api.Modules;
using EduVerse.Api.Security;
using EduVerse.Application.Identity;
using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Infrastructure.MySql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.MapInboundClaims = false;
        var key = builder.Configuration["Authentication:SigningKey"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Authentication:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Authentication:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrEmpty(key) ? null :
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var user = context.Principal;
                if (user is null ||
                    !Guid.TryParse(user.FindFirst("sub")?.Value, out var uid) ||
                    !Guid.TryParse(user.FindFirst("tenant_id")?.Value, out var tid) ||
                    !Guid.TryParse(user.FindFirst("sid")?.Value, out var sid))
                {
                    context.Fail("Invalid identity claims.");
                    return;
                }
                var store = context.HttpContext.RequestServices.GetRequiredService<IIdentityStore>();
                if (!await store.IsSessionValidAsync(sid, tid, uid, context.HttpContext.RequestAborted))
                    context.Fail("Session revoked or membership inactive.");
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<InstitutionService>();
builder.Services.AddScoped<IdentityService>();
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<IAccessTokenIssuer, JwtTokenIssuer>();
builder.Services.AddSingleton<IPasswordResetMessenger, SmtpPasswordResetMessenger>();
builder.Services.AddScoped<IIdentityStore>(provider =>
    new MySqlIdentityStore(provider.GetRequiredService<IConfiguration>().GetConnectionString("EduVerse")
        ?? throw new InvalidOperationException("ConnectionStrings:EduVerse must be configured.")));
builder.Services.AddScoped<IInstitutionRepository>(provider =>
    new MySqlInstitutionRepository(provider.GetRequiredService<IConfiguration>().GetConnectionString("EduVerse")
        ?? throw new InvalidOperationException("ConnectionStrings:EduVerse must be configured.")));

var app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live");

var api = app.MapGroup("/api/v1");
api.MapGet("/system/info", () => TypedResults.Ok(
    new SystemInfoResponse("EduVerse", "v1", DateTimeOffset.UtcNow)))
    .WithName("GetSystemInfo");
api.MapGet("/platform/modules", () => TypedResults.Ok(ModuleRegistry.All))
    .WithName("ListPlatformModules");
app.MapIdentityEndpoints();
app.MapInstitutionEndpoints();
app.Run();

public partial class Program { }
