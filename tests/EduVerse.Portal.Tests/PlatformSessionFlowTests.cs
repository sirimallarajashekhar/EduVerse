using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EduVerse.Portal.Tests;

/// <summary>Verifies the Web cookie routes operator sessions to the correct API validator.</summary>
public sealed class PlatformSessionFlowTests
{
    private sealed class StubIdentityApi:HttpMessageHandler
    {
        public int PlatformSessions;
        public int InstitutionSessions;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(path=="/api/v1/platform-auth/login")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content=JsonContent.Create(new
                    {
                        accessToken="test-operator-token",
                        expiresUtc=DateTimeOffset.UtcNow.AddMinutes(10),
                        displayName="Operator",
                        email="operator@example.org",
                        subjectId=Guid.NewGuid().ToString("D"),
                        tenantId=Guid.Empty,
                        roleCode="PLATFORM_SUPER_ADMIN"
                    })
                });
            if(path=="/api/v1/platform-admin/session")
            {
                PlatformSessions++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            if(path=="/api/v1/identity/session")
            {
                InstitutionSessions++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task SuperAdminLoginKeepsPlatformCookieAndOpensConsole()
    {
        var handler=new StubIdentityApi();
        using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(host=>
            host.ConfigureTestServices(services=>
                services.AddHttpClient("EduVerseIdentityApi")
                    .ConfigurePrimaryHttpMessageHandler(()=>handler)));
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var loginPage=await client.GetAsync("/platform/login");
        Assert.Equal(HttpStatusCode.OK,loginPage.StatusCode);
        var html=await loginPage.Content.ReadAsStringAsync();
        var input=Regex.Match(html,@"<input[^>]*name=""__RequestVerificationToken""[^>]*>");
        Assert.True(input.Success,"Antiforgery form token is required.");
        var token=Regex.Match(input.Value,@"value=""([^""]+)""").Groups[1].Value;
        Assert.NotEmpty(token);
        using var signedIn=await client.PostAsync("/platform/login",new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["Email"]="operator@example.org",["Password"]="dummy-password",
            ["__RequestVerificationToken"]=token
        }));
        Assert.Equal(HttpStatusCode.Redirect,signedIn.StatusCode);
        Assert.Contains("/platform/console",signedIn.Headers.Location?.ToString());
        using var console=await client.GetAsync("/platform/console");
        Assert.Equal(HttpStatusCode.OK,console.StatusCode);
        var consoleHtml=await console.Content.ReadAsStringAsync();
        Assert.Contains("Platform control center",consoleHtml);
        Assert.Contains("All EduVerse modules",consoleHtml);
        Assert.Contains("platform-all-modules",consoleHtml);
        using var root=await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect,root.StatusCode);
        Assert.Equal("/platform/console",root.Headers.Location?.ToString());
        using var institutionLogin=await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.Redirect,institutionLogin.StatusCode);
        Assert.Equal("/platform/console",institutionLogin.Headers.Location?.ToString());
        using var modules=await client.GetAsync("/modules");
        Assert.Equal(HttpStatusCode.Redirect,modules.StatusCode);
        Assert.Equal("/platform/console",modules.Headers.Location?.ToString());
        Assert.True(handler.PlatformSessions>0);
        Assert.Equal(0,handler.InstitutionSessions);
    }
}
