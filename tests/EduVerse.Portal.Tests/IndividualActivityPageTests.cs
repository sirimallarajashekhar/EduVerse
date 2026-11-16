using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class IndividualActivityPageTests
{
    private sealed class TestIdentity(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity=new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"test-renderer"),
                new Claim(ClaimTypes.Name,"Renderer"),new Claim(ClaimTypes.Role,"ADMIN"),
                new Claim(ClaimTypes.Role,"PLATFORM_SUPER_ADMIN")},Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
    private static WebApplicationFactory<Program> Host()=>new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
            services.AddAuthentication(options=>
            {
                options.DefaultAuthenticateScheme="ActivityRenderTest";
                options.DefaultChallengeScheme="ActivityRenderTest";
            }).AddScheme<AuthenticationSchemeOptions,TestIdentity>("ActivityRenderTest",_=>{})));

    [Fact]
    public async Task EveryActivityResponseExcludesOtherActivitiesWithoutJavaScript()
    {
        using var host=Host();using var client=host.CreateClient(new(){AllowAutoRedirect=false});
        foreach(var menu in ActivityCatalog.Menus.Values.Where(x=>x.BasePath.StartsWith("/admin/")||x.BasePath.StartsWith("/portal/")||x.BasePath=="/platform/console"))
        {
            using var hub=await client.GetAsync(menu.BasePath);
            Assert.Equal(HttpStatusCode.OK,hub.StatusCode);
            var hubHtml=await hub.Content.ReadAsStringAsync();
            Assert.DoesNotContain("data-ev-action-key=",hubHtml);
            foreach(var activity in menu.Activities)
            {
                var path=menu.BasePath+"/activities/"+Uri.EscapeDataString(activity.Key);
                Assert.Contains(path,hubHtml);
                using var page=await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK,page.StatusCode);
                var html=await page.Content.ReadAsStringAsync();
                Assert.DoesNotContain("ev-activities=",html);
                var keys=Regex.Matches(html,"data-ev-action-key=\"([^\"]+)\"")
                    .Select(match=>match.Groups[1].Value).Distinct().ToArray();
                Assert.NotEmpty(keys);
                Assert.All(keys,key=>Assert.Equal(activity.Key,key));
            }
        }
    }

    [Fact]
    public async Task RefundAndReconciliationAreDifferentServerRenderedPages()
    {
        using var host=Host();using var client=host.CreateClient();
        var refund=await client.GetStringAsync("/admin/finance/activities/refund-form");
        Assert.Contains("id=\"refund-form\"",refund);
        Assert.DoesNotContain("id=\"reconcile-form\"",refund);
        Assert.DoesNotContain("id=\"payment-form\"",refund);
        var reconcile=await client.GetStringAsync("/admin/finance/activities/reconcile-form");
        Assert.Contains("id=\"reconcile-form\"",reconcile);
        Assert.DoesNotContain("id=\"refund-form\"",reconcile);
        using var unknown=await client.GetAsync("/admin/finance/activities/not-an-activity");
        Assert.Equal(HttpStatusCode.NotFound,unknown.StatusCode);
    }
    [Theory]
    [InlineData("/admin/academics/activities/create-class","item-kind","CLASS")]
    [InlineData("/admin/academics/activities/create-subject","item-kind","SUBJECT")]
    [InlineData("/admin/institution/activities/create-department","resource-type","departments")]
    [InlineData("/admin/institution/activities/create-building","resource-type","buildings")]
    [InlineData("/admin/students/activities/promote-student","lifecycle-action","PROMOTE")]
    [InlineData("/admin/students/activities/transfer-student","lifecycle-action","TRANSFER")]
    [InlineData("/admin/students/activities/withdraw-student","lifecycle-action","WITHDRAW")]
    public async Task SpecificOperationHasNoOperationSwitcher(string route,string field,string value)
    {
        using var host=Host();using var client=host.CreateClient();
        var html=await client.GetStringAsync(route);
        Assert.DoesNotMatch("<select[^>]*id=\""+field+"\"",html);
        var input=Regex.Match(html,"<input[^>]*id=\""+field+"\"[^>]*>").Value;
        Assert.Contains("type=\"hidden\"",input);
        Assert.Contains("value=\""+value+"\"",input);
        if(value=="PROMOTE")
        {
            Assert.Contains("id=\"promote-year\"",html);
            Assert.Contains("id=\"promote-item\"",html);
            Assert.DoesNotContain("id=\"target-campus\"",html);
        }
        if(value=="TRANSFER")
        {
            Assert.Contains("id=\"target-campus\"",html);
            Assert.DoesNotContain("id=\"promote-year\"",html);
        }
        if(value=="WITHDRAW")
        {
            Assert.DoesNotContain("id=\"target-campus\"",html);
            Assert.DoesNotContain("id=\"promote-year\"",html);
        }
    }

    [Theory]
    [InlineData("/admin/academics/activities/catalog-form","/admin/academics")]
    [InlineData("/admin/students/activities/lifecycle-form","/admin/students")]
    [InlineData("/admin/institution/activities/resource-form","/admin/institution")]
    public async Task CombinedOperationBookmarksReturnToActivityMenu(string route,string menu)
    {
        using var host=Host();using var client=host.CreateClient(new(){AllowAutoRedirect=false});
        using var response=await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.Equal(menu,response.Headers.Location?.ToString());
    }
    [Theory]
    [InlineData("/portal/teacher/activities/attendance-form","enrollment")]
    [InlineData("/portal/teacher/activities/marks-form","assessment")]
    [InlineData("/portal/teacher/activities/phase23-assignment-form","p23-lesson")]
    [InlineData("/portal/teacher/activities/phase23-grade-form","p23-submission")]
    [InlineData("/portal/student/activities/phase23-student-submit","p23-assignment")]
    [InlineData("/portal/parent/activities/phase23-parent-message","p23-child")]
    [InlineData("/admin/workflows/activities/binding-form","binding-user")]
    [InlineData("/admin/workflows/activities/purchase-form","purchase-id")]
    public async Task RecordReferencesRenderAsSelectionsRatherThanManualIds(string route,string field)
    {
        using var host=Host();using var client=host.CreateClient();
        var html=await client.GetStringAsync(route);
        Assert.Matches("<select[^>]*id=\""+field+"\"",html);
        Assert.DoesNotMatch("<input[^>]*id=\""+field+"\"",html);
        Assert.DoesNotContain("UUID",html);
        Assert.Contains("record-lookups.js",html);
    }
}
