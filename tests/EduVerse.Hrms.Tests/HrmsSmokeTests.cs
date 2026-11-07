using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Hrms.Tests;

public sealed class HrmsSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public HrmsSmokeTests(WebApplicationFactory<Program> factory) =>
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task HrmsRoot_RendersPreviewWithSharedShell()
    {
        using var response = await _client.GetAsync("/hrms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Human Resource Management", html);
        Assert.Contains("ev-sidebar", html);
    }

    [Fact]
    public async Task Liveness_IsHealthy()
    {
        using var response = await _client.GetAsync("/hrms/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
