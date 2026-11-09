using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
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
        public string? LastCampusRequestBody;
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
            if(path=="/api/v1/tenant/modules")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content=JsonContent.Create(new
                    {
                        tenantId=Guid.NewGuid(),institutionName="Example School",
                        institutionKind="School",role="ADMIN",planCode="PILOT",
                        modules=new[]
                        {
                            new { id="students",name="Admissions & Students",route="/admin/students",
                                description="Student records",feature="core",enabled=true,
                                unavailableReason=(string?)null },
                            new { id="finance",name="Fees & Finance",route="/admin/finance",
                                description="Fee collections",feature="finance",enabled=false,
                                unavailableReason=(string?)"Not included in the active subscription" }
                        }
                    })
                });
            if(path=="/api/v1/institutions/campuses" && request.Method==HttpMethod.Post)
            {
                LastCampusRequestBody=request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content=JsonContent.Create(new {id=Guid.NewGuid(),code="NORTH",name="North Campus"})
                });
            }
            if(path=="/api/v1/institution-admin/dashboard")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content=JsonContent.Create(new
                    {
                        name="Example School",kind="School",onboardingStatus="DRAFT",
                        campuses=2,departments=3,buildings=4,members=7,
                        subscription=new {planCode="PILOT",status="ACTIVE",features=new[]{"core"}}
                    })
                });
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
        Assert.Contains("institution-quick-actions",dashboard);
        Assert.Contains("institution-module-filter",dashboard);
        Assert.Contains("overview-campus-count",dashboard);
        Assert.Contains("overview-member-count",dashboard);
        using var modulesApi=await client.GetAsync("/bff/tenant/modules");
        Assert.Equal(HttpStatusCode.OK,modulesApi.StatusCode);
        var modulesBody=await modulesApi.Content.ReadAsStringAsync();
        using var parsedModules=JsonDocument.Parse(modulesBody);
        var modulesArray=parsedModules.RootElement.GetProperty("modules");
        Assert.Equal("students",modulesArray[0].GetProperty("id").GetString());
        Assert.True(modulesArray[0].GetProperty("enabled").GetBoolean());
        Assert.Equal("finance",modulesArray[1].GetProperty("id").GetString());
        Assert.False(modulesArray[1].GetProperty("enabled").GetBoolean());
        using var overviewApi=await client.GetAsync("/bff/institution-admin/dashboard");
        Assert.Equal(HttpStatusCode.OK,overviewApi.StatusCode);
        var overviewBody=await overviewApi.Content.ReadAsStringAsync();
        Assert.Contains("\"campuses\":2",overviewBody);
        Assert.Contains("\"members\":7",overviewBody);
        using var institutionPage=await client.GetAsync("/admin/institution");
        Assert.Equal(HttpStatusCode.OK,institutionPage.StatusCode);
        var institutionHtml=await institutionPage.Content.ReadAsStringAsync();
        Assert.Contains("campus-form",institutionHtml);
        Assert.Contains("campuses-list",institutionHtml);
        foreach (var page in new[]
        {
            (Route:"/admin/students",Section:"ev-admission",Script:"student-admin.js"),
            (Route:"/admin/academics",Section:"ev-catalog",Script:"academic-admin.js"),
            (Route:"/admin/attendance",Section:"ev-timetable",Script:"attendance-admin.js"),
            (Route:"/admin/finance",Section:"ev-collections",Script:"finance-admin.js"),
            (Route:"/admin/exams",Section:"ev-results",Script:"exam-admin.js")
        })
        {
            using var sectionResponse=await client.GetAsync(page.Route);
            Assert.Equal(HttpStatusCode.OK,sectionResponse.StatusCode);
            var sectionHtml=await sectionResponse.Content.ReadAsStringAsync();
            Assert.Contains("ev-module-workspace",sectionHtml);
            Assert.Contains("ev-module-tabs",sectionHtml);
            Assert.Contains("id=\"" + page.Section + "\"",sectionHtml);
            Assert.Contains(page.Script,sectionHtml);
            Assert.Contains("operational-workspace.js",sectionHtml);
            Assert.Contains("__RequestVerificationToken",sectionHtml);
        }
        var institutionTokenInput=Regex.Match(institutionHtml,
            @"<input[^>]*name=""__RequestVerificationToken""[^>]*>");
        Assert.True(institutionTokenInput.Success);
        var institutionToken=Regex.Match(institutionTokenInput.Value,
            @"value=""([^""]+)""").Groups[1].Value;
        using var campusRequest=new HttpRequestMessage(HttpMethod.Post,"/bff/institution-admin/campuses");
        campusRequest.Headers.Add("RequestVerificationToken",institutionToken);
        campusRequest.Content=JsonContent.Create(new {code="NORTH",name="North Campus"});
        using var campusResponse=await client.SendAsync(campusRequest);
        Assert.Equal(HttpStatusCode.Created,campusResponse.StatusCode);
        Assert.Contains("\"NORTH\"",api.LastCampusRequestBody);

        using var root=await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect,root.StatusCode);
        Assert.Equal("/modules",root.Headers.Location?.ToString());
        using var loginAgain=await client.GetAsync("/account/login");
        Assert.Equal(HttpStatusCode.Redirect,loginAgain.StatusCode);
        Assert.Equal("/modules",loginAgain.Headers.Location?.ToString());
        Assert.True(api.TenantSessions>0);
    }
}
