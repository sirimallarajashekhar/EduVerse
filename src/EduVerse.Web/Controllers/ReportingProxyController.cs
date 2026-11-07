using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin allowlisted BFF; bearer token and tenant authorization stay in the backend API.</summary>
[Authorize(Roles = "ADMIN")]
[ApiController]
[AutoValidateAntiforgeryToken]
[Route("bff/reporting")]
public sealed class ReportingProxyController(IHttpClientFactory clients) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<IActionResult> Dashboard(Guid campusId, DateOnly from, DateOnly to, CancellationToken ct) =>
        Forward("dashboard", campusId, from, to, false, ct);

    [HttpGet("monthly.csv")]
    public Task<IActionResult> Csv(Guid campusId, DateOnly from, DateOnly to, CancellationToken ct) =>
        Forward("monthly.csv", campusId, from, to, true, ct);

    private async Task<IActionResult> Forward(string route, Guid campus, DateOnly from, DateOnly to,
        bool download, CancellationToken ct)
    {
        if (campus == Guid.Empty) return BadRequest();
        var token = await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme, "access_token");
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/v1/reporting/{route}?campusId={campus:D}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var result = await clients.CreateClient("EduVerseIdentityApi").SendAsync(request, ct);
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (download && result.IsSuccessStatusCode)
                return File(await result.Content.ReadAsByteArrayAsync(ct),
                    "text/csv; charset=utf-8", $"eduverse-report-{campus:N}.csv");
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
