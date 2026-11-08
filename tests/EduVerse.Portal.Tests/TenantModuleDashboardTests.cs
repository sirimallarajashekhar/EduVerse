using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class TenantModuleDashboardTests:IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public TenantModuleDashboardTests(WebApplicationFactory<Program> factory)=>
        client=factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});

    [Fact]
    public async Task AnonymousDashboardDoesNotExposeAdminLinks()
    {
        using var response=await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();
        Assert.Contains("Register school",html);
        Assert.DoesNotContain("Open AMS",html);
        Assert.DoesNotContain("Institution Management</a>",html);
    }

    [Fact]
    public async Task TenantModuleBffRequiresAuthentication()
    {
        using var response=await client.GetAsync("/bff/tenant/modules");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
}
