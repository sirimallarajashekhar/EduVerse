using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class RbacAdminPageTests:IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public RbacAdminPageTests(WebApplicationFactory<Program> factory)=>
        client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});

    [Fact] public async Task AnonymousCannotOpenRoleEditor()
    {
        using var response=await client.GetAsync("/admin/rbac");
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.Contains("/account/login",response.Headers.Location?.ToString());
    }
    [Fact] public async Task AnonymousCannotReadRoleManagementBff()
    {
        using var response=await client.GetAsync("/bff/rbac/roles");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
    [Fact] public async Task RoleManagementMutationRequiresAuthenticationAndCsrf()
    {
        using var response=await client.PostAsJsonAsync("/bff/rbac/roles",
            new {code="TEACHER",displayName="Teacher"});
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
    }
}
