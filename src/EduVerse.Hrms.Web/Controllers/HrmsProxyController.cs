using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Hrms.Web.Controllers;
/// <summary>Independent HRMS BFF. API exclusively owns HRMS security, transactions and calculations.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("hrms/bff")]
public sealed class HrmsProxyController(IHttpClientFactory clients):ControllerBase
{
 private static readonly HashSet<string> GetRoutes=new(StringComparer.Ordinal)
   {"designations","employees","leave-types","leaves","attendance","dashboard"};
 private static readonly HashSet<string> PostRoutes=new(StringComparer.Ordinal)
   {"designations","employees","leave-types","leaves","salaries","payroll/preview"};
 [HttpGet("departments")]
 public async Task<IActionResult> Departments(Guid campusId,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(HttpMethod.Get,
   $"api/v1/institution-admin/resources/department?campusId={campusId:D}");
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  return await Send(req,ct);
 }
 [HttpGet("campuses")]
 public async Task<IActionResult> Campuses(CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(HttpMethod.Get,"api/v1/institutions/campuses");
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  return await Send(req,ct);
 }
 [HttpGet("{path}")]
 public Task<IActionResult> Get(string path,Guid campusId,DateOnly? date,CancellationToken ct)=>
   Forward(HttpMethod.Get,path,campusId,date,null,ct);
 [HttpPost("{path}")]
 public Task<IActionResult> Post(string path,[FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
   Forward(HttpMethod.Post,path,null,null,body.GetRawText(),ct);
 [HttpPost("payroll/preview")]
 public Task<IActionResult> Preview(Guid campusId,Guid employeeId,DateOnly month,CancellationToken ct)=>
   Forward(HttpMethod.Post,$"payroll/preview?campusId={campusId:D}&employeeId={employeeId:D}&month={month:yyyy-MM-dd}",
    null,null,null,ct);
 [HttpPost("employees/{employee:guid}/lifecycle")]
 public Task<IActionResult> Lifecycle(Guid employee,Guid campusId,
  [FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
   Forward(HttpMethod.Post,$"employees/{employee:D}/lifecycle?campusId={campusId:D}",
    null,null,body.GetRawText(),ct);
 [HttpPost("leaves/{id:guid}/decision")]
 public Task<IActionResult> Decision(Guid id,Guid campusId,
  [FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
   Forward(HttpMethod.Post,$"leaves/{id:D}/decision?campusId={campusId:D}",null,null,body.GetRawText(),ct);
 [HttpGet("employees/{employee:guid}/documents")]
 public Task<IActionResult> Documents(Guid employee,Guid campusId,CancellationToken ct)=>
   Forward(HttpMethod.Get,$"employees/{employee:D}/documents",campusId,null,null,ct);
 [HttpGet("employees/{employee:guid}/leave-balance")]
 public Task<IActionResult> Balance(Guid employee,Guid campusId,Guid leaveTypeId,int year,CancellationToken ct)=>
   Forward(HttpMethod.Get,
    $"employees/{employee:D}/leave-balance?campusId={campusId:D}&leaveTypeId={leaveTypeId:D}&year={year}",
    null,null,null,ct);
 [HttpPost("employees/{employee:guid}/documents")]
 public async Task<IActionResult> Upload(Guid employee,Guid campusId,string type,IFormFile file,CancellationToken ct)
 {
  if(file.Length is < 4 or > 1048576)return BadRequest();
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(HttpMethod.Post,
   $"api/v1/hrms/employees/{employee:D}/documents?campusId={campusId:D}&type={Uri.EscapeDataString(type)}");
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  using var multipart=new MultipartFormDataContent();
  var bytes=new StreamContent(file.OpenReadStream());
  bytes.Headers.ContentType=new MediaTypeHeaderValue(file.ContentType);
  multipart.Add(bytes,"file",file.FileName);
  req.Content=multipart;
  return await Send(req,ct);
 }
 [HttpGet("employees/{employee:guid}/documents/{document:guid}/download")]
 public async Task<IActionResult> Download(Guid employee,Guid document,Guid campusId,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var req=new HttpRequestMessage(HttpMethod.Get,
   $"api/v1/hrms/employees/{employee:D}/documents/{document:D}/download?campusId={campusId:D}");
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);
   if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode);
   Response.Headers["X-Content-Type-Options"]="nosniff";
   Response.Headers["Cache-Control"]="no-store";
   return File(await response.Content.ReadAsByteArrayAsync(ct),"application/octet-stream","hr-document");
  }
  catch(HttpRequestException){return StatusCode(502);}
 }
 private async Task<IActionResult> Forward(HttpMethod method,string path,Guid? campus,DateOnly? date,string? body,CancellationToken ct)
 {
  if(method==HttpMethod.Get && !GetRoutes.Contains(path) &&
    !path.StartsWith("employees/",StringComparison.Ordinal))return NotFound();
  if(method==HttpMethod.Post && !PostRoutes.Contains(path) &&
    !path.StartsWith("employees/",StringComparison.Ordinal) &&
    !path.StartsWith("leaves/",StringComparison.Ordinal))return NotFound();
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  var uri="api/v1/hrms/"+path;
  if(campus is Guid id)
  {
   uri+=(uri.Contains('?')?"&":"?")+"campusId="+id.ToString("D");
   if(date is DateOnly d)uri+="&date="+d.ToString("yyyy-MM-dd");
  }
  using var req=new HttpRequestMessage(method,uri);
  req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(body is not null)req.Content=new StringContent(body,System.Text.Encoding.UTF8,"application/json");
  return await Send(req,ct);
 }
 private async Task<IActionResult> Send(HttpRequestMessage req,CancellationToken ct)
 {
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(req,ct);
   return new ContentResult{StatusCode=(int)response.StatusCode,
    ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await response.Content.ReadAsStringAsync(ct)};
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
