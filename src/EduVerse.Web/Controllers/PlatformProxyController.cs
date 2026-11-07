using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin presentation proxy for the Platform API; no module-business logic.</summary>
[ApiController]
[Route("bff/platform")]
public sealed class PlatformProxyController(IHttpClientFactory clients) : ControllerBase
{
    [HttpGet("modules")]
    public async Task<IActionResult> Modules(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await clients.CreateClient("EduVerseApi").GetAsync("api/v1/platform/modules", cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json; charset=utf-8",
                Content = payload
            };
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Platform API unavailable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "Platform API timed out");
        }
    }
}
