using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class PlatformApprovalPageTests:IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public PlatformApprovalPageTests(WebApplicationFactory<Program> factory)=>
        client=factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});

    [Fact]
    public async Task OperatorLoginFormIsAvailableWithAntiForgery()
    {
        using var response=await client.GetAsync("/platform/login");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();
        Assert.Contains("EduVerse Platform Super Admin",html);
        Assert.Contains("__RequestVerificationToken",html);
    }

    [Fact]
    public async Task AnonymousCannotViewQueue()
    {
        using var response=await client.GetAsync("/platform/registrations");
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.Contains("/account/login",response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task ReviewPostRequiresAntiForgery()
    {
        using var response=await client.PostAsync(
            "/platform/registrations/"+Guid.NewGuid().ToString("D")+"/approve",
            new FormUrlEncodedContent(new Dictionary<string,string>{{"reason","Test"}}));
        Assert.True(response.StatusCode==HttpStatusCode.BadRequest ||
                    response.StatusCode==HttpStatusCode.Redirect);
    }
}
