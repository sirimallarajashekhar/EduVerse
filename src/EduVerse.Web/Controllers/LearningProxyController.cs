using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;
/// <summary>Same-origin, anti-CSRF and allowlisted; all learning rules are API-owned.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/learning")]
public sealed class LearningProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> Resources=new(StringComparer.Ordinal)
 {"lessons","assignments","submissions","announcements","threads","messages","notifications","dashboard"};
 [HttpGet("{resource}")]
 public Task<IActionResult> Read(string resource,Guid campusId,CancellationToken ct)=>
   Resources.Contains(resource)&&resource!="messages"
    ?Forward(HttpMethod.Get,$"{resource}?campusId={campusId:D}",null,ct)
    :Task.FromResult<IActionResult>(NotFound());
 [HttpPost("{resource}")]
 public Task<IActionResult> Create(string resource,[FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
   Resources.Contains(resource)&&resource is not ("dashboard" or "notifications")
    ?Forward(HttpMethod.Post,resource,body.GetRawText(),ct)
    :Task.FromResult<IActionResult>(NotFound());
 [HttpPost("{resource}/{id:guid}/publish")]
 public Task<IActionResult> Publish(string resource,Guid id,Guid campusId,CancellationToken ct)=>
   resource is "lessons" or "assignments"
    ?Forward(HttpMethod.Post,$"{resource}/{id:D}/publish?campusId={campusId:D}",null,ct)
    :Task.FromResult<IActionResult>(NotFound());
 [HttpPost("submissions/{id:guid}/grade")]
 public Task<IActionResult> Grade(Guid id,Guid campusId,[FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
   Forward(HttpMethod.Post,$"submissions/{id:D}/grade?campusId={campusId:D}",body.GetRawText(),ct);
 [HttpGet("enrollments/{id:guid}/progress")]
 public Task<IActionResult> Progress(Guid id,Guid campusId,CancellationToken ct)=>
   Forward(HttpMethod.Get,$"enrollments/{id:D}/progress?campusId={campusId:D}",null,ct);
 [HttpGet("threads/{id:guid}/messages")]
 public Task<IActionResult> Thread(Guid id,Guid campusId,CancellationToken ct)=>
   Forward(HttpMethod.Get,$"threads/{id:D}/messages?campusId={campusId:D}",null,ct);
 [HttpPost("notifications/{id:guid}/read")]
 public Task<IActionResult> ReadNotification(Guid id,Guid campusId,CancellationToken ct)=>
   Forward(HttpMethod.Post,$"notifications/{id:D}/read?campusId={campusId:D}",null,ct);
 private async Task<IActionResult> Forward(HttpMethod method,string path,string? payload,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var request=new HttpRequestMessage(method,"api/v1/learning/"+path);
  request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(payload is not null)request.Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json");
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
   Response.Headers["Cache-Control"]="no-store";
   return new ContentResult{StatusCode=(int)response.StatusCode,
    ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await response.Content.ReadAsStringAsync(ct)};
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
