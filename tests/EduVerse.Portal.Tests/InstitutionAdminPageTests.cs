using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class InstitutionAdminPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public InstitutionAdminPageTests(WebApplicationFactory<Program> factory) =>
        client=factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});

    [Fact]
    public async Task PublicRegistrationFormIsAvailable()
    {
        using var response=await client.GetAsync("/register-institution");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();
        Assert.Contains("Request institution registration",html);
        Assert.Contains("__RequestVerificationToken",html);
    }
    [Fact]
    public async Task AnonymousInstitutionDashboardRedirectsToLogin()
    {
        using var response=await client.GetAsync("/admin/institution");
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.Contains("/account/login",response.Headers.Location?.ToString());
    }
    [Fact]
    public async Task PublicRegistrationPostRequiresAntiForgery()
    {
        using var response=await client.PostAsync("/register-institution",
            new FormUrlEncodedContent(new Dictionary<string,string>
            {["Slug"]="demo-school",["Name"]="Demo School",["Kind"]="School",["Email"]="admin@example.org"}));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }
}
