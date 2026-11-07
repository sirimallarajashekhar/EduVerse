using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;
/// <summary>Thin cookie BFF; forwards only a closed list of college REST routes.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/college")]
public sealed class CollegeProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> Resources=new(StringComparer.Ordinal)
 {"regulations","programs","semesters","courses","registrations","transcripts","report"};
 [HttpGet("{resource}")]
 public Task<IActionResult> Get(string resource,Guid campusId,Guid? enrollmentId,CancellationToken ct)=>
  Resources.Contains(resource)?Forward(HttpMethod.Get,
    $"{resource}?campusId={campusId:D}"+(enrollmentId is Guid id?$"&enrollmentId={id:D}":""),null,ct)
   :Task.FromResult<IActionResult>(NotFound());
 [HttpPost("{resource}")]
 public Task<IActionResult> Post(string resource,[FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Resources.Contains(resource)&&resource is not ("transcripts" or "report")
   ?Forward(HttpMethod.Post,resource,body.GetRawText(),ct)
   :Task.FromResult<IActionResult>(NotFound());
 [HttpPost("grades/import")]
 public Task<IActionResult> Import([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"grades/import",body.GetRawText(),ct);
 [HttpPost("semesters/finalize")]
 public Task<IActionResult> Finalize([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"semesters/finalize",body.GetRawText(),ct);
 private async Task<IActionResult> Forward(HttpMethod method,string route,string? payload,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(method,"api/v1/college/"+route);
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(payload is not null)req.Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json");
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);
   Response.Headers["Cache-Control"]="no-store";
   return new ContentResult{StatusCode=(int)response.StatusCode,
    ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await response.Content.ReadAsStringAsync(ct)};
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
