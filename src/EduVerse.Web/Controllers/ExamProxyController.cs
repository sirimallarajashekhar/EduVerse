using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Strict same-origin allowlist, CSRF enforcement and backend-only domain decisions.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/exams")]
public sealed class ExamProxyController(IHttpClientFactory clients):ControllerBase
{
 [HttpGet("")]
 public Task<IActionResult> Exams(Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,"",campusId,null,null,ct);
 [HttpPost("")]
 public Task<IActionResult> Create([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"",null,body.GetRawText(),null,ct);
 [HttpGet("schemes")]
 public Task<IActionResult> Schemes(Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,"schemes",campusId,null,null,ct);
 [HttpPost("schemes")]
 public Task<IActionResult> Scheme([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"schemes",null,body.GetRawText(),null,ct);
 [HttpGet("assessments")]
 public Task<IActionResult> Assessments(Guid campusId,Guid examId,CancellationToken ct)=>
  Forward(HttpMethod.Get,"assessments",campusId,null,new Dictionary<string,string>{{"examId",examId.ToString("D")}},ct);
 [HttpPost("assessments")]
 public Task<IActionResult> Assessment([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"assessments",null,body.GetRawText(),null,ct);
 [HttpGet("marks")]
 public Task<IActionResult> Marks(Guid campusId,Guid assessmentId,CancellationToken ct)=>
  Forward(HttpMethod.Get,"marks",campusId,null,new Dictionary<string,string>{{"assessmentId",assessmentId.ToString("D")}},ct);
 [HttpPost("marks")]
 public Task<IActionResult> Mark([FromBody]System.Text.Json.JsonElement body,CancellationToken ct)=>
  Forward(HttpMethod.Post,"marks",null,body.GetRawText(),null,ct);
 [HttpPost("{id:guid}/approve")]
 public Task<IActionResult> Approve(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Post,id.ToString("D")+"/approve",campusId,null,null,ct);
 [HttpPost("{id:guid}/publish")]
 public Task<IActionResult> Publish(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Post,id.ToString("D")+"/publish",campusId,null,null,ct);
 [HttpGet("{id:guid}/results/{enrollment:guid}")]
 public Task<IActionResult> Result(Guid id,Guid enrollment,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,id.ToString("D")+"/results/"+enrollment.ToString("D"),campusId,null,null,ct);
 [HttpGet("{id:guid}/analytics")]
 public Task<IActionResult> Analytics(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,id.ToString("D")+"/analytics",campusId,null,null,ct);
 [HttpGet("students/{id:guid}/cgpa")]
 public Task<IActionResult> Cgpa(Guid id,Guid campusId,CancellationToken ct)=>
  Forward(HttpMethod.Get,"students/"+id.ToString("D")+"/cgpa",campusId,null,null,ct);
 [HttpGet("{id:guid}/results/{enrollment:guid}/download")]
 public async Task<IActionResult> Download(Guid id,Guid enrollment,Guid campusId,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  using var request=new HttpRequestMessage(HttpMethod.Get,
   $"api/v1/exams/{id:D}/results/{enrollment:D}/download?campusId={campusId:D}");
  request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
   if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode);
   Response.Headers["Cache-Control"]="no-store";
   Response.Headers["X-Content-Type-Options"]="nosniff";
   return File(await response.Content.ReadAsByteArrayAsync(ct),"text/plain; charset=utf-8","eduverse-report-"+id.ToString("N")+".txt");
  }
  catch(HttpRequestException){return StatusCode(502);}
 }
 private async Task<IActionResult> Forward(HttpMethod method,string route,Guid? campus,
   string? json,Dictionary<string,string>? query,CancellationToken ct)
 {
  var token=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
  if(string.IsNullOrWhiteSpace(token))return Unauthorized();
  var path="api/v1/exams"+(route==""?"":"/"+route);
  var parameters=new List<string>();
  if(campus is Guid id)parameters.Add("campusId="+id.ToString("D"));
  if(query is not null)foreach(var (key,value) in query)
    parameters.Add(Uri.EscapeDataString(key)+"="+Uri.EscapeDataString(value));
  if(parameters.Count>0)path+="?"+string.Join("&",parameters);
  using var request=new HttpRequestMessage(method,path);
  request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  if(json is not null)request.Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json");
  try
  {
   using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
   return new ContentResult{
    StatusCode=(int)response.StatusCode,
    ContentType=response.Content.Headers.ContentType?.ToString()??"application/json",
    Content=await response.Content.ReadAsStringAsync(ct)};
  }
  catch(HttpRequestException){return StatusCode(502);}
  catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
 }
}
