using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class PortalSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PortalSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task Home_RendersSharedNavigationAndModuleCards()
    {
        using var response = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Institution command center", html);
        Assert.Contains("Super Admin login", html);
        Assert.Contains("/js/site.js", html);
        Assert.DoesNotContain("id=\"module-navigation\"", html);
        Assert.Contains("Register school", html);
        Assert.DoesNotContain("Open AMS", html);
    }
}
