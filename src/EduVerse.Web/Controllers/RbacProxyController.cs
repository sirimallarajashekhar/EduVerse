using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Allowlisted same-origin adapter. No RBAC policy logic or database access in MVC.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[Route("bff/rbac")]
public sealed class RbacProxyController(IHttpClientFactory clients):ControllerBase
{
    [HttpGet("roles")] public Task<IActionResult> Roles(CancellationToken ct)=>
        Forward(HttpMethod.Get,"roles",null,ct);
    [HttpGet("assignments")] public Task<IActionResult> Assignments(CancellationToken ct)=>
        Forward(HttpMethod.Get,"assignments",null,ct);
    [HttpGet("catalog")] public Task<IActionResult> Catalog(CancellationToken ct)=>
        Forward(HttpMethod.Get,"catalog",null,ct);
    [HttpPost("roles"),ValidateAntiForgeryToken]
    public Task<IActionResult> Create([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
        Forward(HttpMethod.Post,"roles",body.GetRawText(),ct);
    [HttpPut("roles/{code}/permissions"),ValidateAntiForgeryToken]
    public Task<IActionResult> Permissions(string code,[FromBody]System.Text.Json.JsonElement body,CancellationToken ct)
    {
        if(!SafeCode(code))return Task.FromResult<IActionResult>(BadRequest());
        return Forward(HttpMethod.Put,"roles/"+code+"/permissions",body.GetRawText(),ct);
    }
    [HttpPost("assignments"),ValidateAntiForgeryToken]
    public Task<IActionResult> Assign([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
        Forward(HttpMethod.Post,"assignments",body.GetRawText(),ct);
    [HttpDelete("assignments/{userId:guid}/{roleCode}"),ValidateAntiForgeryToken]
    public Task<IActionResult> Remove(Guid userId,string roleCode,CancellationToken ct)
    {
        if(!SafeCode(roleCode))return Task.FromResult<IActionResult>(BadRequest());
        return Forward(HttpMethod.Delete,$"assignments/{userId:D}/{roleCode}",null,ct);
    }
    private static bool SafeCode(string value)=>value.Length is >=3 and <=32 &&
        value.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    private async Task<IActionResult> Forward(HttpMethod method,string path,string? json,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        using var message=new HttpRequestMessage(method,"api/v1/rbac/"+path);
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(json is not null)message.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(message,ct);
            return new ContentResult {StatusCode=(int)response.StatusCode,
                ContentType="application/json; charset=utf-8",
                Content=await response.Content.ReadAsStringAsync(ct)};
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
