using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using EduVerse.AuthWeb;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Server-rendered BFF; forwards operator bearer token, never accesses MySQL.</summary>
[Route("platform")]
public sealed class PlatformAdminController(IHttpClientFactory clients):Controller
{
    [AllowAnonymous,HttpGet("login")]
    public IActionResult Login()=>View(new PlatformLoginForm());

    [AllowAnonymous,HttpPost("login"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(PlatformLoginForm form,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(form.Email)||string.IsNullOrWhiteSpace(form.Password))
        {
            ModelState.AddModelError("","Email and password are required.");
            return View(form);
        }
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi")
                .PostAsJsonAsync("api/v1/platform-auth/login",new {form.Email,form.Password},ct);
            if(!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("","Invalid operator credentials.");
                return View(form);
            }
            var ticket=await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken:ct);
            if(ticket is null||ticket.RoleCode!="PLATFORM_SUPER_ADMIN")return StatusCode(502);
            var identity=new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier,ticket.SubjectId),
                new Claim(ClaimTypes.Name,ticket.DisplayName),
                new Claim(ClaimTypes.Email,ticket.Email),
                new Claim(ClaimTypes.Role,"PLATFORM_SUPER_ADMIN")
            },EduVerseCookieAuth.Scheme);
            var properties=new AuthenticationProperties {IsPersistent=false,ExpiresUtc=ticket.ExpiresUtc,AllowRefresh=false};
            properties.StoreTokens(new[] {new AuthenticationToken{Name="access_token",Value=ticket.AccessToken}});
            await HttpContext.SignInAsync(EduVerseCookieAuth.Scheme,new ClaimsPrincipal(identity),properties);
            return RedirectToAction("Index","PlatformConsole");
        }
        catch(HttpRequestException)
        {
            ModelState.AddModelError("","Approval service is unavailable.");
            return View(form);
        }
    }

    [Authorize(Roles="PLATFORM_SUPER_ADMIN"),HttpGet("registrations")]
    public async Task<IActionResult> Registrations(string status="PENDING",CancellationToken ct=default)
    {
        if(status is not ("PENDING" or "APPROVED" or "REJECTED"))return BadRequest();
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/v1/platform-admin/registrations?status="+status);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode);
            var rows=await response.Content.ReadFromJsonAsync<List<PlatformRegistrationView>>(cancellationToken:ct);
            return View(new PlatformRegistrationPage(status,rows??new()));
        }
        catch(HttpRequestException){return StatusCode(502);}
    }

    [Authorize(Roles="PLATFORM_SUPER_ADMIN"),HttpPost("registrations/{id:guid}/{actionName:regex(^(approve|reject)$)}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(Guid id,string actionName,string reason,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(reason)||reason.Length>500)
        {
            TempData["Error"]="Enter a reason of 1–500 characters.";
            return RedirectToAction(nameof(Registrations));
        }
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        using var request=new HttpRequestMessage(HttpMethod.Post,
            $"api/v1/platform-admin/registrations/{id:D}/{actionName}");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        request.Content=JsonContent.Create(new {reason});
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            TempData[response.IsSuccessStatusCode?"Info":"Error"]=response.IsSuccessStatusCode
                ? "Registration reviewed successfully."
                : response.StatusCode==System.Net.HttpStatusCode.Conflict
                    ? "Registration is already reviewed or its slug is occupied."
                    : "Review failed; verify operator permissions and registration status.";
        }
        catch(HttpRequestException){TempData["Error"]="Approval API is unavailable.";}
        return RedirectToAction(nameof(Registrations));
    }

    [Authorize(Roles="PLATFORM_SUPER_ADMIN"),HttpPost("logout"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        using var req=new HttpRequestMessage(HttpMethod.Post,"api/v1/platform-admin/logout");
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        try {using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);}
        catch(HttpRequestException){}
        await HttpContext.SignOutAsync(EduVerseCookieAuth.Scheme);
        return RedirectToAction(nameof(Login));
    }
}
public sealed class PlatformLoginForm
{
    public string Email {get;set;}=string.Empty;
    public string Password {get;set;}=string.Empty;
}
public sealed record PlatformRegistrationView(Guid Id,string Slug,string Name,string Kind,string Email,
    string Status,DateTime CreatedUtc,DateTime? ReviewedUtc,string? Reason,Guid? TenantId);
public sealed record PlatformRegistrationPage(string Status,IReadOnlyList<PlatformRegistrationView> Requests);
