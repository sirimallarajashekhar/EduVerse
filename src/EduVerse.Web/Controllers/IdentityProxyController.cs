using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Presentation-only API proxy. No role grants or tenant decisions happen here.</summary>
[Authorize]
[ApiController]
[Route("bff/identity")]
public sealed class IdentityProxyController(IHttpClientFactory factory) : ControllerBase
{
    [HttpGet("users")]
    public Task<IActionResult> Users(CancellationToken ct) => ForwardAsync(HttpMethod.Get, "users", null, ct);

    [HttpPost("users"), ValidateAntiForgeryToken]
    public Task<IActionResult> Invite([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, "users", body.GetRawText(), ct);

    [HttpPost("permissions"), ValidateAntiForgeryToken]
    public Task<IActionResult> Permissions([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, "permissions", body.GetRawText(), ct);

    [HttpPost("campus-access"), ValidateAntiForgeryToken]
    public Task<IActionResult> CampusAccess([FromBody] System.Text.Json.JsonElement body, CancellationToken ct)
        => ForwardAsync(HttpMethod.Post, "campus-access", body.GetRawText(), ct);

    private async Task<IActionResult> ForwardAsync(HttpMethod method, string segment, string? body, CancellationToken ct)
    {
        var bearer = await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme, "access_token");
        if (string.IsNullOrWhiteSpace(bearer)) return Unauthorized();
        using var request = new HttpRequestMessage(method, "api/v1/identity/" + segment);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        try
        {
            using var result = await factory.CreateClient("EduVerseIdentityApi").SendAsync(request, ct);
            return new ContentResult
            {
                StatusCode = (int)result.StatusCode,
                ContentType = "application/json; charset=utf-8",
                Content = await result.Content.ReadAsStringAsync(ct)
            };
        }
        catch (HttpRequestException) { return StatusCode(502); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return StatusCode(504); }
    }
}
