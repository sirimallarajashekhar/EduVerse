using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Allowlisted same-origin API adapter; CSRF protected for all portal mutations.</summary>
[Authorize, ApiController, AutoValidateAntiforgeryToken]
[Route("bff/portals")]
public sealed class RolePortalProxyController(IHttpClientFactory clients):ControllerBase
{
    private static readonly HashSet<string> General = new(StringComparer.Ordinal)
    {
        "parent/children","student/me"
    };
    private static readonly HashSet<string> Teacher = new(StringComparer.Ordinal)
    {
        "teacher/classes","teacher/lessons"
    };
    private static readonly HashSet<string> StudentSections = new(StringComparer.Ordinal)
    {
        "attendance","results","timetable","assignments","notices"
    };
    private static readonly HashSet<string> ParentSections = new(StringComparer.Ordinal)
    {
        "attendance","results","notices"
    };

    [HttpGet("{**route}")]
    public Task<IActionResult> Get(string route,Guid? campusId,CancellationToken ct)
    {
        route=(route??"").Trim('/');
        if(General.Contains(route))return Send(HttpMethod.Get,route,null,null,ct);
        if(campusId is not Guid campus || campus==Guid.Empty)return Task.FromResult<IActionResult>(BadRequest());
        if(Teacher.Contains(route))return Send(HttpMethod.Get,route,campus,null,ct);
        var bits=route.Split('/');
        if(bits.Length==4 && bits[0]=="teacher" && bits[1]=="classes" &&
            Guid.TryParse(bits[2],out var classId) && bits[3]=="students")
            return Send(HttpMethod.Get,$"teacher/classes/{classId:D}/students",campus,null,ct);
        if(bits.Length==4 && bits[0]=="parent" && bits[1]=="children" &&
           Guid.TryParse(bits[2],out var parentStudent) && ParentSections.Contains(bits[3]))
            return Send(HttpMethod.Get,$"parent/children/{parentStudent:D}/{bits[3]}",campus,null,ct);
        if(bits.Length==3 && bits[0]=="student" &&
           Guid.TryParse(bits[1],out var ownStudent) && StudentSections.Contains(bits[2]))
            return Send(HttpMethod.Get,$"student/{ownStudent:D}/{bits[2]}",campus,null,ct);
        return Task.FromResult<IActionResult>(NotFound());
    }
    [HttpPost("teacher/lessons")]
    public Task<IActionResult> CreateLesson([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
        Send(HttpMethod.Post,"teacher/lessons",null,body.GetRawText(),ct);

    [HttpPost("teacher/attendance")]
    public Task<IActionResult> Attendance([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
        Send(HttpMethod.Post,"teacher/attendance",null,body.GetRawText(),ct);

    [HttpPost("teacher/marks")]
    public Task<IActionResult> Marks([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
        Send(HttpMethod.Post,"teacher/marks",null,body.GetRawText(),ct);

    private async Task<IActionResult> Send(HttpMethod method,string route,Guid? campus,string? body,CancellationToken ct)
    {
        var bearer=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(bearer))return Unauthorized();
        var url="api/v1/portals/"+route+(campus is Guid c?"?campusId="+c.ToString("D"):"");
        using var request=new HttpRequestMessage(method,url);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        if(body is not null)request.Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json");
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            Response.Headers["Cache-Control"]="no-store";
            return new ContentResult
            {
                StatusCode=(int)response.StatusCode,
                ContentType="application/json; charset=utf-8",
                Content=await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
