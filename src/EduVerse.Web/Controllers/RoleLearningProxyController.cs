using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>CSRF-verified, explicit path allowlist; JWT forwarded only to EduVerse API.</summary>
[Authorize,ApiController,AutoValidateAntiforgeryToken]
[Route("bff/role-learning")]
public sealed class RoleLearningProxyController(IHttpClientFactory clients):ControllerBase
{
    private static readonly HashSet<string> GetRoutes=new(StringComparer.Ordinal)
    {
        "teacher/submissions","conversations","admin/reviews","notifications"
    };
    private static readonly HashSet<string> PostRoutes=new(StringComparer.Ordinal)
    {
        "teacher/assignments","teacher/grade","student/submit",
        "parent/conversations","conversations/reply",
        "admin/reviews/LESSON/publish","admin/reviews/ASSIGNMENT/publish"
    };
    [HttpGet("{**path}")]
    public Task<IActionResult> Get(string path,Guid? campusId,CancellationToken ct)
    {
        path=(path??"").Trim('/');
        if(campusId is not Guid campus||campus==Guid.Empty)return Task.FromResult<IActionResult>(BadRequest());
        if(GetRoutes.Contains(path))return Forward(HttpMethod.Get,path,campus,null,ct);
        var parts=path.Split('/');
        if(parts.Length==3 && parts[0]=="student" && Guid.TryParse(parts[1],out var student) &&
           parts[2]=="submissions")
            return Forward(HttpMethod.Get,$"student/{student:D}/submissions",campus,null,ct);
        if(parts.Length==3 && parts[0]=="conversations" && Guid.TryParse(parts[1],out var thread) &&
           parts[2]=="messages")
            return Forward(HttpMethod.Get,$"conversations/{thread:D}/messages",campus,null,ct);
        return Task.FromResult<IActionResult>(NotFound());
    }
    [HttpPost("{**path}")]
    public Task<IActionResult> Post(string path,[FromBody]System.Text.Json.JsonElement body,CancellationToken ct)
    {
        path=(path??"").Trim('/');
        var segments=path.Split('/');
        if(segments.Length==3 && segments[0]=="notifications" &&
           Guid.TryParse(segments[1],out var notificationId) && segments[2]=="read")
            return Forward(HttpMethod.Post,$"notifications/{notificationId:D}/read",null,body.GetRawText(),ct);
        if(!PostRoutes.Contains(path))return Task.FromResult<IActionResult>(NotFound());
        return Forward(HttpMethod.Post,path,null,body.GetRawText(),ct);
    }
    private async Task<IActionResult> Forward(HttpMethod method,string path,Guid? campus,string? json,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        using var message=new HttpRequestMessage(method,"api/v1/role-learning/"+path+
            (campus is Guid c?"?campusId="+c.ToString("D"):""));
        message.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(json is not null)message.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(message,ct);
            Response.Headers["Cache-Control"]="no-store";
            return new ContentResult{
                StatusCode=(int)response.StatusCode,
                ContentType="application/json; charset=utf-8",
                Content=await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
