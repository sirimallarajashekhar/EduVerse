using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles="PLATFORM_SUPER_ADMIN")]
[Route("platform")]
public sealed class PlatformConsoleController:Controller
{
    [HttpGet("console")] public IActionResult Index()=>View();
}

/// <summary>Strict same-origin allowlist. Operator bearer token is never exposed to JavaScript.</summary>
[Authorize(Roles="PLATFORM_SUPER_ADMIN"),ApiController,AutoValidateAntiforgeryToken]
[Route("bff/platform-control")]
public sealed class PlatformControlProxyController(IHttpClientFactory clients):ControllerBase
{
    [HttpGet("institutions")]
    public Task<IActionResult> Institutions(CancellationToken ct)=>
        Forward(HttpMethod.Get,"institutions",null,ct);

    [HttpGet("institutions/{tenantId:guid}/modules")]
    public Task<IActionResult> Modules(Guid tenantId,CancellationToken ct)=>
        Forward(HttpMethod.Get,$"institutions/{tenantId:D}/modules",null,ct);

    [HttpGet("institutions/{tenantId:guid}/users")]
    public Task<IActionResult> Users(Guid tenantId,CancellationToken ct)=>
        Forward(HttpMethod.Get,$"institutions/{tenantId:D}/users",null,ct);

    [HttpPost("institutions/{tenantId:guid}/users")]
    public Task<IActionResult> Invite(Guid tenantId,[FromBody]System.Text.Json.JsonElement json,CancellationToken ct)=>
        Forward(HttpMethod.Post,$"institutions/{tenantId:D}/users",json.GetRawText(),ct);

    [HttpPost("institutions/{tenantId:guid}/users/{userId:guid}/deactivate")]
    public Task<IActionResult> Deactivate(Guid tenantId,Guid userId,
        [FromBody]System.Text.Json.JsonElement json,CancellationToken ct)=>
        Forward(HttpMethod.Post,$"institutions/{tenantId:D}/users/{userId:D}/deactivate",json.GetRawText(),ct);

    private async Task<IActionResult> Forward(HttpMethod method,string path,string? json,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        using var req=new HttpRequestMessage(method,"api/v1/platform-control/"+path);
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(json is not null)req.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);
            Response.Headers["Cache-Control"]="no-store";
            return new ContentResult{StatusCode=(int)response.StatusCode,
                ContentType="application/json; charset=utf-8",
                Content=await response.Content.ReadAsStringAsync(ct)};
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
