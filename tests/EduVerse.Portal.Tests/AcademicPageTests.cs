using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;
public sealed class AcademicPageTests:IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public AcademicPageTests(WebApplicationFactory<Program> factory)=>
        client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});

    [Fact]
    public async Task AnonymousAcademicAdministrationRedirectsToLogin()
    {
        using var result=await client.GetAsync("/admin/academics");
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        Assert.Contains("/account/login",result.Headers.Location?.ToString());
    }
    [Fact]
    public async Task AnonymousAcademicBffCannotRead()
    {
        using var result=await client.GetAsync("/bff/academic/catalog?campusId="+Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
    }
}
