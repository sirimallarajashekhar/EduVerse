using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Ams.Web.Controllers;

/// <summary>Fixed-route BFF, CSRF-protected; forwards the signed-in user JWT for API-owned security.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("ams/bff")]
public sealed class AmsProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> GetPaths=new(StringComparer.Ordinal)
 {"categories","vendors","assets","maintenance","stock","purchases","dashboard"};
 private static readonly HashSet<string> PostPaths=new(StringComparer.Ordinal)
 {"categories","vendors","assets","assets/allocate","assets/lifecycle",
  "maintenance","stock","stock/movements","purchases"};

 [HttpGet("campuses")]
 public Task<IActionResult> Campuses(CancellationToken ct)=>Forward(HttpMethod.Get,
  "api/v1/institutions/campuses",null,null,ct);
 [HttpGet("{path}")]
 public Task<IActionResult> Get(string path,Guid campusId,CancellationToken ct)
 {
  if(!GetPaths.Contains(path))return Task.FromResult<IActionResult>(NotFound());
  return Forward(HttpMethod.Get,$"api/v1/ams/{path}?campusId={campusId:D}",null,null,ct);
 }
 [HttpPost("{path}")]
 public Task<IActionResult> Post(string path,[FromBody]System.Text.Json.JsonElement body,
  [FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct)
 {
  if(!PostPaths.Contains(path))return Task.FromResult<IActionResult>(NotFound());
  return Forward(HttpMethod.Post,"api/v1/ams/"+path,body.GetRawText(),key,ct);
 }
 [HttpPost("assets/allocate")]
 public Task<IActionResult> Allocate([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"api/v1/ams/assets/allocate",body.GetRawText(),null,ct);
 [HttpPost("assets/lifecycle")]
 public Task<IActionResult> Lifecycle([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"api/v1/ams/assets/lifecycle",body.GetRawText(),null,ct);
 [HttpPost("stock/movements")]
 public Task<IActionResult> Movement([FromBody]System.Text.Json.JsonElement body,
  [FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct)=>
  Forward(HttpMethod.Post,"api/v1/ams/stock/movements",body.GetRawText(),key,ct);
 [HttpGet("assets/{asset:guid}/events")]
 public Task<IActionResult> Events(Guid asset,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,$"api/v1/ams/assets/{asset:D}/events?campusId={campusId:D}",null,null,ct);
 [HttpPost("maintenance/{id:guid}/complete")]
 public Task<IActionResult> Complete(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Post,$"api/v1/ams/maintenance/{id:D}/complete?campusId={campusId:D}",null,null,ct);

 private async Task<IActionResult> Forward(HttpMethod method,string path,string? payload,string? key,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(method,path);
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(key is not null)req.Headers.TryAddWithoutValidation("Idempotency-Key",key);
  if(payload is not null)
   req.Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json");
  try
  {
   using var res=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);
   Response.Headers["Cache-Control"]="no-store";
   return new ContentResult{
    StatusCode=(int)res.StatusCode,
    ContentType=res.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await res.Content.ReadAsStringAsync(ct)};
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
