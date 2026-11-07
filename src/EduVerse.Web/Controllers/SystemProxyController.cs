using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin same-origin proxy. No persistence, validation or business decisions belong here.</summary>
[ApiController]
[Route("bff/system")]
public sealed class SystemProxyController(IHttpClientFactory clients) : ControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> Info(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/system/info");
        using var response = await clients.CreateClient("EduVerseApi").SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,
            ContentType = "application/json; charset=utf-8",
            Content = json
        };
    }
}
