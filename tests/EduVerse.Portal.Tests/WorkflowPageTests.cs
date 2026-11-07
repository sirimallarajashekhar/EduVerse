using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class WorkflowPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public WorkflowPageTests(WebApplicationFactory<Program> factory) =>
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task AnonymousWorkflowPageRequiresSignIn()
    {
        using var response = await client.GetAsync("/admin/workflows");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task AnonymousCannotReadClearance()
    {
        using var response = await client.GetAsync(
            $"/bff/workflows/employees/{Guid.NewGuid():D}/clearance?campusId={Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCannotTriggerExitWithoutAuthenticationOrCsrf()
    {
        using var response = await client.PostAsync("/bff/workflows/employees/offboard",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
    }
}
