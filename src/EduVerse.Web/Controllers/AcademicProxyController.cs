using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Same-origin, allowlisted BFF. The REST API enforces admin, tenant, campus and subscription access.</summary>
[Authorize]
[ApiController]
[Route("bff/academic")]
[AutoValidateAntiforgeryToken]
public sealed class AcademicProxyController(IHttpClientFactory clients):ControllerBase
{
    private static readonly HashSet<string> Resources = new(StringComparer.Ordinal)
    { "years","catalog","faculty-assignments","calendar","overview" };
    [HttpGet("{resource}")]
    public Task<IActionResult> Get(string resource,Guid campusId,string? kind,CancellationToken ct) =>
        Forward(HttpMethod.Get,resource,Json:null,campusId,kind,ct);

    [HttpPost("{resource}")]
    public Task<IActionResult> Post(string resource,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,resource,body.GetRawText(),null,null,ct);

    private async Task<IActionResult> Forward(HttpMethod method,string resource,string? Json,
        Guid? campusId,string? kind,CancellationToken ct)
    {
        if(!Resources.Contains(resource) || (method==HttpMethod.Post && resource=="overview"))
            return NotFound();
        var bearer=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(bearer))return Unauthorized();
        var route="api/v1/academic/"+resource;
        if(method==HttpMethod.Get)
        {
            if(campusId is null || campusId==Guid.Empty)return BadRequest();
            route+="?campusId="+campusId.Value.ToString("D");
            if(kind is not null)route+="&kind="+Uri.EscapeDataString(kind);
        }
        using var request=new HttpRequestMessage(method,route);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        if(Json is not null)request.Content=new StringContent(Json,System.Text.Encoding.UTF8,"application/json");
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            return new ContentResult
            {
                StatusCode=(int)response.StatusCode,
                ContentType=response.Content.Headers.ContentType?.ToString() ?? "application/json",
                Content=await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
