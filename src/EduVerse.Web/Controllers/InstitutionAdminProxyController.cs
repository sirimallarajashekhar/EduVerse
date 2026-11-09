using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin same-origin BFF. Whitelisted paths; API owns validation and tenant authorization.</summary>
[Authorize(Roles="ADMIN")]
[AutoValidateAntiforgeryToken]
[ApiController]
[Route("bff/institution-admin")]
public sealed class InstitutionAdminProxyController(IHttpClientFactory clients):ControllerBase
{
    private static readonly HashSet<string> AllowedGet = new(StringComparer.Ordinal)
    {
        "dashboard","profile","settings","plans","subscription","campuses",
        "resources/departments","resources/buildings"
    };
    private static readonly HashSet<string> AllowedChange = new(StringComparer.Ordinal)
    {
        "profile","settings","onboarding/ready","campuses",
        "resources/departments","resources/buildings"
    };

    [HttpGet("{**path}")]
    public Task<IActionResult> Get(string path,CancellationToken ct)
        => Forward(HttpMethod.Get,path,null,ct);

    [HttpPut("{**path}")]
    public Task<IActionResult> Put(string path,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct)
        => Forward(HttpMethod.Put,path,body.GetRawText(),ct);

    [HttpPost("campuses")]
    public Task<IActionResult> CreateCampus([FromBody] System.Text.Json.JsonElement body,CancellationToken ct)
        => Forward(HttpMethod.Post,"campuses",body.GetRawText(),ct);

    [HttpPost("{**path}")]
    public Task<IActionResult> Post(string path,CancellationToken ct)
        => Forward(HttpMethod.Post,path,null,ct);

    [HttpPost("{type:regex(^(departments|buildings)$)}/new")]
    public Task<IActionResult> AddResource(string type,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct)
        => Forward(HttpMethod.Post,"resources/"+type,body.GetRawText(),ct);

    private async Task<IActionResult> Forward(HttpMethod method,string path,string? json,CancellationToken ct)
    {
        path=path?.Trim('/') ?? "";
        if(method==HttpMethod.Get && !AllowedGet.Contains(path) ||
            method!=HttpMethod.Get && !AllowedChange.Contains(path) ||
            method==HttpMethod.Post && path!="onboarding/ready" && path!="campuses" && path!="resources/departments" && path!="resources/buildings")
            return NotFound();

        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        var target=path=="campuses" ? "api/v1/institutions/campuses"
            : "api/v1/institution-admin/"+path;
        using var request=new HttpRequestMessage(method,target);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(json is not null)
            request.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
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
        catch(HttpRequestException) {return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested) {return StatusCode(504);}
    }
}
