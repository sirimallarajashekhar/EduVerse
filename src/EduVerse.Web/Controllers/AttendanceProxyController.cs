using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Strict allowlist and anti-forgery. API is the sole business-rule and authorization authority.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/attendance")]
public sealed class AttendanceProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> Paths=new(StringComparer.Ordinal)
 { "periods","timetable","students","students/summary","staff","dashboard" };

 [HttpGet("{**path}")]
 public Task<IActionResult> Get(string path,Guid campusId,Guid? academicYearId,Guid? enrollmentId,DateOnly? date,CancellationToken ct)=>
  Forward(HttpMethod.Get,path,campusId,academicYearId,enrollmentId,date,null,ct);

 [HttpPost("{**path}")]
 public Task<IActionResult> Post(string path,[FromBody] System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,path,Guid.Empty,null,null,null,body.GetRawText(),ct);

 private async Task<IActionResult> Forward(HttpMethod method,string path,Guid campus,
    Guid? year,Guid? enrollment,DateOnly? date,string? json,CancellationToken ct)
 {
  path=(path??"").Trim('/');
  if(!Paths.Contains(path)||(method==HttpMethod.Post && path is not ("periods" or "timetable" or "students" or "staff")))
   return NotFound();
  if(method==HttpMethod.Get && campus==Guid.Empty)return BadRequest();
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  var url="api/v1/attendance/"+path;
  if(method==HttpMethod.Get)
  {
   url+="?campusId="+campus.ToString("D");
   if(year is Guid y)url+="&academicYearId="+y.ToString("D");
   if(enrollment is Guid e)url+="&enrollmentId="+e.ToString("D");
   if(date is DateOnly d)url+="&date="+d.ToString("yyyy-MM-dd");
  }
  using var request=new HttpRequestMessage(method,url);
  request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(json is not null)request.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
   return new ContentResult{
    StatusCode=(int)response.StatusCode,
    ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await response.Content.ReadAsStringAsync(ct)
   };
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
