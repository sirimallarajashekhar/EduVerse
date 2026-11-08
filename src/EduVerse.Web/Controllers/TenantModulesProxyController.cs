using System.Net.Http.Headers;
using EduVerse.AuthWeb;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Same-origin thin proxy: API alone chooses tenant modules and access.</summary>
[Authorize(Roles="ADMIN")]
[ApiController]
[Route("bff/tenant/modules")]
public sealed class TenantModulesProxyController(IHttpClientFactory clients):ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var bearer=await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme,"access_token");
        if(string.IsNullOrWhiteSpace(bearer)) return Unauthorized();
        using var request=new HttpRequestMessage(HttpMethod.Get,"api/v1/tenant/modules");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        try
        {
            using var response=await clients.CreateClient("EduVerseIdentityApi").SendAsync(request,ct);
            return new ContentResult {
                StatusCode=(int)response.StatusCode,
                ContentType="application/json; charset=utf-8",
                Content=await response.Content.ReadAsStringAsync(ct)
            };
        }
        catch(HttpRequestException){return StatusCode(502);}
        catch(TaskCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504);}
    }
}
