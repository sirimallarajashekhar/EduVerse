using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Ams.Tests;

public sealed class AmsSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public AmsSmokeTests(WebApplicationFactory<Program> factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task AmsRoot_RendersPreviewWithSharedShell()
    {
        using var response = await _client.GetAsync("/ams");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Asset Management System", html);
        Assert.Contains("ev-sidebar", html);
    }

    [Fact]
    public async Task Liveness_IsHealthy()
    {
        using var response = await _client.GetAsync("/ams/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
