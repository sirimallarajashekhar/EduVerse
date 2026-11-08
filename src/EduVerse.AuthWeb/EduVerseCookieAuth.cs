using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace EduVerse.AuthWeb;

/// <summary>Shared SSO cookie on the same domain; API validates sessions and all business permissions.</summary>
public static class EduVerseCookieAuth
{
    public const string Scheme = "EduVerse.Cookie";

    public static IServiceCollection AddEduVerseCookieAuthentication(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        // All portal/module instances must use the SAME shared key ring and application name.
        var location = configuration["Auth:KeyRingPath"];
        if (string.IsNullOrWhiteSpace(location))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Auth:KeyRingPath must point to a persistent shared key store.");
            location = Path.Combine(Path.GetTempPath(), "eduverse-sso-keyring");
        }
        Directory.CreateDirectory(location);
        services.AddDataProtection().SetApplicationName("EduVerse.SharedSSO")
            .PersistKeysToFileSystem(new DirectoryInfo(location));
        services.AddHttpClient("EduVerseIdentityApi", client =>
        {
            var url = configuration["Api:BaseUrl"]
                ?? throw new InvalidOperationException("Api:BaseUrl must be configured for SSO.");
            client.BaseAddress = new Uri(url, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(8);
        });
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = ".EduVerse.Auth";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest
                : Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            options.LoginPath = "/account/login";
            options.Events.OnRedirectToLogin = context =>
            {
                var isPlatformPath=context.Request.Path.StartsWithSegments("/platform")
                    || context.Request.Path.StartsWithSegments("/bff/platform-control");
                var redirect=isPlatformPath?"/platform/login":"/account/login";
                if(context.Request.Path.StartsWithSegments("/bff") ||
                   context.Request.Path.Value?.Contains("/bff/",StringComparison.OrdinalIgnoreCase)==true)
                {
                    context.Response.StatusCode=StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(redirect+"?returnUrl="+Uri.EscapeDataString(
                    context.Request.PathBase.ToString()+context.Request.Path.ToString()+context.Request.QueryString.ToString()));
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode=StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
            options.AccessDeniedPath = "/account/denied";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = async context =>
            {
                var bearer = context.Properties.GetTokenValue("access_token");
                if (string.IsNullOrWhiteSpace(bearer))
                {
                    context.RejectPrincipal();
                    return;
                }
                try
                {
                    var client = context.HttpContext.RequestServices
                        .GetRequiredService<IHttpClientFactory>().CreateClient("EduVerseIdentityApi");
                    var isPlatformOperator=context.Principal?.IsInRole("PLATFORM_SUPER_ADMIN")==true;
                    var sessionPath=isPlatformOperator
                        ? "api/v1/platform-admin/session"
                        : "api/v1/identity/session";
                    using var request = new HttpRequestMessage(HttpMethod.Get, sessionPath);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                    using var response = await client.SendAsync(request, context.HttpContext.RequestAborted);
                    if (!response.IsSuccessStatusCode) context.RejectPrincipal();
                }
                catch (HttpRequestException) { context.RejectPrincipal(); }
                catch (TaskCanceledException) { context.RejectPrincipal(); }
            };
        });
        services.AddAuthorization();
        return services;
    }
}
