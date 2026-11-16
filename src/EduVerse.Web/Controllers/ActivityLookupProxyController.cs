using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduVerse.Web.Controllers;

[Authorize,ApiController]
[Route("bff/activity-lookups")]
public sealed class ActivityLookupProxyController(IHttpClientFactory clients):ControllerBase
{
    private static readonly HashSet<string> Allowed=new(StringComparer.Ordinal)
    {"campuses","linked-students","teacher-contexts","teacher-assessments","teacher-periods","teacher-lessons","teacher-submissions","employees","users","purchases"};
    [HttpGet("{resource}")]
    public async Task<IActionResult> Get(string resource,Guid? campusId,string? q,CancellationToken ct)
    {
        if(!Allowed.Contains(resource))return NotFound();
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        var path="api/v1/activity-lookups/"+resource+"?q="+Uri.EscapeDataString(q??string.Empty);
        if(campusId is Guid campus)path+="&campusId="+campus.ToString("D");
        using var request=new HttpRequestMessage(HttpMethod.Get,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            Response.Headers["Cache-Control"]="no-store";
            return new ContentResult{StatusCode=(int)response.StatusCode,ContentType="application/json",Content=await response.Content.ReadAsStringAsync(ct)};
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
