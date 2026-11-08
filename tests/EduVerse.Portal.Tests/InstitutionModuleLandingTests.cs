using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduVerse.Portal.Tests;

/// <summary>Valid tenant login must open the API-owned module dashboard, not a login page.</summary>
public sealed class InstitutionModuleLandingTests
{
    private sealed class StubTenantIdentityApi:HttpMessageHandler
    {
        public int TenantSessions;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path=="/api/v1/identity/login")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content=JsonContent.Create(new
                    {
                        accessToken="tenant-test-token",
                        expiresUtc=DateTimeOffset.UtcNow.AddMinutes(10),
                        displayName="Institution administrator",
                        email="admin@example.org",
                        subjectId=Guid.NewGuid().ToString("D"),
                        tenantId=Guid.NewGuid(),
                        roleCode="ADMIN"
                    })
                });
            if(path=="/api/v1/identity/session")
            {
                TenantSessions++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task ValidInstitutionAdminLoginRedirectsToModulesAndStaysAuthenticated()
    {
        var api=new StubTenantIdentityApi();
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(host=>
            host.ConfigureTestServices(services=>
                services.AddHttpClient("EduVerseIdentityApi")
                    .ConfigurePrimaryHttpMessageHandler(()=>api)));
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var login=await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var html=await login.Content.ReadAsStringAsync();
        var input=Regex.Match(html,@"<input[^>]*name=""__RequestVerificationToken""[^>]*>");
        Assert.True(input.Success);
        var csrf=Regex.Match(input.Value,@"value=""([^""]+)""").Groups[1].Value;
        Assert.NotEmpty(csrf);
        using var authenticated=await client.PostAsync("/account/login",
            new FormUrlEncodedContent(new Dictionary<string,string>
            {
                ["TenantSlug"]="test-school",
                ["Email"]="admin@example.org",
                ["Password"]="test-password",
                ["__RequestVerificationToken"]=csrf
            }));
        Assert.Equal(HttpStatusCode.Redirect,authenticated.StatusCode);
        Assert.Equal("/modules",authenticated.Headers.Location?.ToString());
        using var modulePage=await client.GetAsync("/modules");
        Assert.Equal(HttpStatusCode.OK,modulePage.StatusCode);
        var dashboard=await modulePage.Content.ReadAsStringAsync();
        Assert.Contains("Available modules",dashboard);
        Assert.Contains("tenant-module-cards",dashboard);
        using var loginAgain=await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.Redirect,loginAgain.StatusCode);
        Assert.Equal("/modules",loginAgain.Headers.Location?.ToString());
        Assert.True(api.TenantSessions>0);
    }
}
