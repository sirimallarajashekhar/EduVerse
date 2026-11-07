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
    public async Task AnonymousUser_MustSignInBeforeOpeningHrms()
    {
        using var response = await _client.GetAsync("/hrms");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Liveness_IsHealthy()
    {
        using var response = await _client.GetAsync("/hrms/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
