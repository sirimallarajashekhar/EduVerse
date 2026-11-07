using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;
/// <summary>Thin allowlisted BFF. API enforces all campus scoping and operational rules.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/operations")]
public sealed class OperationsProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> Resources=new(StringComparer.Ordinal)
 {"titles","copies","loans","reservations","vehicles","routes","stops","route-assignments",
  "hostel-blocks","hostel-rooms","hostel-beds","hostel-allocations","dashboard"};

 [HttpGet("{resource}")]
 public Task<IActionResult> Get(string resource,Guid campusId,CancellationToken ct)=>
  Resources.Contains(resource)?Forward(HttpMethod.Get,$"{resource}?campusId={campusId:D}",null,ct)
    :Task.FromResult<IActionResult>(NotFound());

 [HttpPost("{resource}")]
 public Task<IActionResult> Post(string resource,[FromBody]System.Text.Json.JsonElement json,CancellationToken ct)=>
  Resources.Contains(resource)&&resource!="dashboard"
   ?Forward(HttpMethod.Post,resource,json.GetRawText(),ct)
   :Task.FromResult<IActionResult>(NotFound());

 [HttpPost("{resource}/{id:guid}/{action}")]
 public Task<IActionResult> Action(string resource,Guid id,string action,Guid campusId,CancellationToken ct)
 {
  var allowed=resource switch
  {
   "loans"=>action=="return",
   "route-assignments"=>action=="release",
   "hostel-allocations"=>action=="vacate",
   _=>false
  };
  return allowed?Forward(HttpMethod.Post,$"{resource}/{id:D}/{action}?campusId={campusId:D}",null,ct)
   :Task.FromResult<IActionResult>(NotFound());
 }
 private async Task<IActionResult> Forward(HttpMethod method,string path,string? payload,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(method,"api/v1/operations/"+path);
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
