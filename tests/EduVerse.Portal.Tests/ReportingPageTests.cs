using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class ReportingPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public ReportingPageTests(WebApplicationFactory<Program> factory) =>
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task AnonymousDashboardRedirectsToSignIn()
    {
        using var response = await client.GetAsync("/admin/reporting");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString());
    }
    [Fact]
    public async Task AnonymousCannotDownloadReportsThroughBff()
    {
        using var response = await client.GetAsync(
            $"/bff/reporting/monthly.csv?campusId={Guid.NewGuid():D}&from=2026-10-01&to=2026-10-31");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task AnonymousCannotReadDashboardThroughBff()
    {
        using var response = await client.GetAsync(
            $"/bff/reporting/dashboard?campusId={Guid.NewGuid():D}&from=2026-10-01&to=2026-10-31");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
