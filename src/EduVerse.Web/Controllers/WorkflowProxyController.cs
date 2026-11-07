using System.Net.Http.Headers;
using System.Text;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Only forwards explicitly allowlisted workflow APIs; the API owns all business decisions.</summary>
[Authorize(Roles = "ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/workflows")]
public sealed class WorkflowProxyController(IHttpClientFactory clients) : ControllerBase
{
    [HttpGet("employees/{employeeId:guid}/clearance")]
    public Task<IActionResult> Clearance(Guid employeeId, Guid campusId, CancellationToken ct) =>
        Forward(HttpMethod.Get,
            $"employees/{employeeId:D}/clearance?campusId={campusId:D}", null, null, ct);

    [HttpPost("employees/bind-identity")]
    public Task<IActionResult> Bind([FromBody] System.Text.Json.JsonElement request,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct) =>
        Forward(HttpMethod.Post, "employees/bind-identity", request.GetRawText(), key, ct);

    [HttpPost("employees/offboard")]
    public Task<IActionResult> Exit([FromBody] System.Text.Json.JsonElement request,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct) =>
        Forward(HttpMethod.Post, "employees/offboard", request.GetRawText(), key, ct);

    [HttpPost("procurement/submit-finance")]
    public Task<IActionResult> Finance([FromBody] System.Text.Json.JsonElement request,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct) =>
        Forward(HttpMethod.Post, "procurement/submit-finance", request.GetRawText(), key, ct);

    private async Task<IActionResult> Forward(HttpMethod method, string route, string? payload,
        string? key, CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme, "access_token");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        using var outgoing = new HttpRequestMessage(method, "api/v1/workflows/" + route);
        outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (key is not null) outgoing.Headers.Add("Idempotency-Key", key);
        if (payload is not null)
            outgoing.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            using var result = await clients.CreateClient("EduVerseIdentityApi").SendAsync(outgoing, ct);
            Response.Headers["Cache-Control"] = "no-store";
            return new ContentResult
            {
                StatusCode = (int)result.StatusCode,
                ContentType = result.Content.Headers.ContentType?.ToString() ?? "application/json",
                Content = await result.Content.ReadAsStringAsync(ct)
            };
        }
        catch (HttpRequestException) { return StatusCode(502); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(504); }
    }
}
