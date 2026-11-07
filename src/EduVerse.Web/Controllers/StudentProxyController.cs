using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>All BFF requests are same-origin and CSRF protected. Never trusts tenant IDs.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[Route("bff/students")]
[AutoValidateAntiforgeryToken]
public sealed class StudentProxyController(IHttpClientFactory clients):ControllerBase
{
    [HttpGet("")]
    public Task<IActionResult> Students(Guid campusId,CancellationToken ct) =>
        Forward(HttpMethod.Get,"",campusId,null,ct);
    [HttpPost("")]
    public Task<IActionResult> Register([FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,"",null,body.GetRawText(),ct);
    [HttpGet("applications")]
    public Task<IActionResult> Applications(Guid campusId,CancellationToken ct) =>
        Forward(HttpMethod.Get,"applications",campusId,null,ct);
    [HttpPost("applications")]
    public Task<IActionResult> Apply([FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,"applications",null,body.GetRawText(),ct);
    [HttpPost("applications/{id:guid}/decision")]
    public Task<IActionResult> Decide(Guid id,Guid campusId,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,$"applications/{id:D}/decision",campusId,body.GetRawText(),ct);
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Student(Guid id,Guid campusId,CancellationToken ct) =>
        Forward(HttpMethod.Get,id.ToString("D"),campusId,null,ct);
    [HttpGet("{id:guid}/guardians")]
    public Task<IActionResult> Guardians(Guid id,Guid campusId,CancellationToken ct) =>
        Forward(HttpMethod.Get,$"{id:D}/guardians",campusId,null,ct);
    [HttpPost("{id:guid}/guardians")]
    public Task<IActionResult> AddGuardian(Guid id,Guid campusId,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,$"{id:D}/guardians",campusId,body.GetRawText(),ct);
    [HttpGet("{id:guid}/enrollments")]
    public Task<IActionResult> Enrollments(Guid id,Guid campusId,CancellationToken ct) =>
        Forward(HttpMethod.Get,$"{id:D}/enrollments",campusId,null,ct);
    [HttpPost("{id:guid}/enrollments")]
    public Task<IActionResult> Enroll(Guid id,Guid campusId,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,$"{id:D}/enrollments",campusId,body.GetRawText(),ct);
    [HttpPost("{id:guid}/lifecycle")]
    public Task<IActionResult> Lifecycle(Guid id,Guid campusId,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,$"{id:D}/lifecycle",campusId,body.GetRawText(),ct);
    [HttpGet("{id:guid}/documents")]
    public Task<IActionResult> Documents(Guid id,Guid campusId,CancellationToken ct) =>
        Forward(HttpMethod.Get,$"{id:D}/documents",campusId,null,ct);
    [HttpPost("{id:guid}/documents")]
    public async Task<IActionResult> Upload(Guid id,Guid campusId,string category,IFormFile file,CancellationToken ct)
    {
        if(file.Length is < 4 or > 1048576)return BadRequest();
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        using var request=new HttpRequestMessage(HttpMethod.Post,
            $"api/v1/students/{id:D}/documents?campusId={campusId:D}&category={Uri.EscapeDataString(category)}");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var multipart=new MultipartFormDataContent();
        multipart.Add(new StreamContent(file.OpenReadStream()),"file",file.FileName);
        multipart.Last().Headers.ContentType=new MediaTypeHeaderValue(file.ContentType);
        request.Content=multipart;
        return await Send(request,ct);
    }
    [HttpGet("{id:guid}/documents/{document:guid}")]
    public async Task<IActionResult> Download(Guid id,Guid document,Guid campusId,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        using var request=new HttpRequestMessage(HttpMethod.Get,
            $"api/v1/students/{id:D}/documents/{document:D}?campusId={campusId:D}");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode);
            var bytes=await response.Content.ReadAsByteArrayAsync(ct);
            Response.Headers["X-Content-Type-Options"]="nosniff";
            return File(bytes,"application/octet-stream","student-document");
        }
        catch(HttpRequestException){return StatusCode(502);}
    }
    [HttpPost("{id:guid}/documents/{document:guid}/review")]
    public Task<IActionResult> Review(Guid id,Guid document,Guid campusId,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct) =>
        Forward(HttpMethod.Post,$"{id:D}/documents/{document:D}/review",campusId,body.GetRawText(),ct);

    private async Task<IActionResult> Forward(HttpMethod method,string route,Guid? campus,string? json,CancellationToken ct)
    {
        var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(token))return Unauthorized();
        var path="api/v1/students"+(route==""?"":"/"+route);
        if(campus is Guid id)path+="?campusId="+id.ToString("D");
        using var request=new HttpRequestMessage(method,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(json is not null)request.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
        return await Send(request,ct);
    }
    private async Task<IActionResult> Send(HttpRequestMessage request,CancellationToken ct)
    {
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            return new ContentResult
            {
                StatusCode=(int)response.StatusCode,
                ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
                Content=await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
