using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Allowlisted same-origin finance BFF; anti-forgery and backend tenant-admin checks apply.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/finance")]
public sealed class FinanceProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> GetRoutes=new(StringComparer.Ordinal)
 { "fees","charges","payments","dashboard" };
 private static readonly HashSet<string> PostRoutes=new(StringComparer.Ordinal)
 { "fees","charges","concessions","payments","refunds","reconciliation" };

 [HttpGet("{route}")]
 public Task<IActionResult> Get(string route,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,route,campusId,null,null,ct);

 [HttpGet("charges/{id:guid}/installments")]
 public Task<IActionResult> Installments(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,$"charges/{id:D}/installments",campusId,null,null,ct);

 [HttpGet("payments/{id:guid}/receipt")]
 public Task<IActionResult> Receipt(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,$"payments/{id:D}/receipt",campusId,null,null,ct);

 [HttpPost("{route}")]
 public Task<IActionResult> Post(string route,[FromBody]System.Text.Json.JsonElement payload,
  [FromHeader(Name="Idempotency-Key")]string? key,CancellationToken ct)=>
  Forward(HttpMethod.Post,route,null,payload.GetRawText(),key,ct);

 private async Task<IActionResult> Forward(HttpMethod method,string route,Guid? campus,
  string? payload,string? key,CancellationToken ct)
 {
  if(method==HttpMethod.Get && !GetRoutes.Contains(route) &&
     !(route.StartsWith("charges/",StringComparison.Ordinal)&&route.EndsWith("/installments",StringComparison.Ordinal))&&
     !(route.StartsWith("payments/",StringComparison.Ordinal)&&route.EndsWith("/receipt",StringComparison.Ordinal)))
   return NotFound();
  if(method==HttpMethod.Post && !PostRoutes.Contains(route))return NotFound();
  if(method==HttpMethod.Get && (!campus.HasValue||campus==Guid.Empty))return BadRequest();
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrEmpty(token))return Unauthorized();
  var path="api/v1/finance/"+route+(campus is Guid id?"?campusId="+id.ToString("D"):"");
  using var req=new HttpRequestMessage(method,path);
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(key is not null)req.Headers.TryAddWithoutValidation("Idempotency-Key",key);
  if(payload is not null)req.Content=new StringContent(payload,System.Text.Encoding.UTF8,"application/json");
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);
   return new ContentResult{
    StatusCode=(int)response.StatusCode,
    ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await response.Content.ReadAsStringAsync(ct)};
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
