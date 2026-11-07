using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class LoginPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public LoginPageTests(WebApplicationFactory<Program> factory) =>
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task LoginForm_LoadsWithoutSigningIn()
    {
        using var response = await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Sign in to EduVerse", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task LoginPost_WithoutAntiforgeryToken_IsRejected()
    {
        using var response = await client.PostAsync("/account/login",
            new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["TenantSlug"]="example", ["Email"]="fake@example.org", ["Password"]="ignored"
            }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousPortalAccessToAms_IsRedirected()
    {
        using var response = await client.GetAsync("/ams");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location?.ToString());
    }
}
