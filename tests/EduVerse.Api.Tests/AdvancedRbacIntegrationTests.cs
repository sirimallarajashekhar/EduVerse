using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Application.Identity;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;

/// <summary>Real MySQL tenant isolation, custom-role grants, campus scope and session revocation.</summary>
public sealed class AdvancedRbacIntegrationTests:IAsyncLifetime
{
    private readonly string? connection=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenant=Guid.NewGuid(),foreign=Guid.NewGuid();
    private readonly Guid admin=Guid.NewGuid(),member=Guid.NewGuid(),foreignUser=Guid.NewGuid();
    private readonly Guid campus=Guid.NewGuid(),otherCampus=Guid.NewGuid();
    private readonly string slug="rbac-"+Guid.NewGuid().ToString("N");
    private readonly string otherSlug="rbac-other-"+Guid.NewGuid().ToString("N");
    private readonly string adminEmail=Guid.NewGuid().ToString("N")+"@example.org";
    private readonly string memberEmail=Guid.NewGuid().ToString("N")+"@example.org";
    private readonly string otherEmail=Guid.NewGuid().ToString("N")+"@example.org";
    private const string Password="RBAC-Test-Password-2026!";
    private WebApplicationFactory<Program>? factory;

    private async Task Exec(string sql,params (string Key,object Value)[] values)
    {
        await using var db=new MySqlConnection(connection);await db.OpenAsync();
        await using var cmd=new MySqlCommand(sql,db);
        foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task InitializeAsync()
    {
        if(string.IsNullOrWhiteSpace(connection))return;
        foreach(var (id,code) in new[]{(tenant,slug),(foreign,otherSlug)})
            await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'RBAC School','School')",
                ("@id",id.ToString("D")),("@slug",code));
        var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
        foreach(var (id,email) in new[]{(admin,adminEmail),(member,memberEmail),(foreignUser,otherEmail)})
            await Exec("""
                INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
                VALUES(@id,@email,@norm,'RBAC Test User',@hash)
                """,("@id",id.ToString("D")),("@email",email),("@norm",email.ToUpperInvariant()),("@hash",hash));
        await Exec("""
            INSERT INTO tenant_memberships(tenant_id,subject_id,role_code)
            VALUES(@tid,@admin,'ADMIN'),(@tid,@member,'MEMBER'),(@other,@foreign,'MEMBER')
            """,("@tid",tenant.ToString("D")),("@admin",admin.ToString("D")),("@member",member.ToString("D")),
            ("@other",foreign.ToString("D")),("@foreign",foreignUser.ToString("D")));
        await Exec("""
            INSERT INTO campuses(id,tenant_id,code,display_name)
            VALUES(@c,@tid,'MAIN','Main'),(@otherCampus,@other,'MAIN','Foreign')
            """,("@c",campus.ToString("D")),("@tid",tenant.ToString("D")),
            ("@otherCampus",otherCampus.ToString("D")),("@other",foreign.ToString("D")));
        factory=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
            web.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(
                new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",connection}})));
    }

    private async Task<HttpClient> Login(Guid who)
    {
        var client=factory!.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});
        var (tenantSlug,email)=who==admin?(slug,adminEmail):
            who==member?(slug,memberEmail):(otherSlug,otherEmail);
        using var response=await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(tenantSlug,email,Password));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var ticket=await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
        return client;
    }

    [Fact]
    public async Task CustomRolesRespectTenantCampusAndImmediateRevocation()
    {
        if(factory is null)return;
        using var adminClient=await Login(admin);
        using var memberClient=await Login(member);
        using var denied=await memberClient.GetAsync("/api/v1/rbac/roles");
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);

        using var created=await adminClient.PostAsJsonAsync("/api/v1/rbac/roles",
            new CreateRbacRole("TEACHER","Classroom Teacher"));
        Assert.Equal(HttpStatusCode.NoContent,created.StatusCode);
        using var forbiddenSystem=await adminClient.PostAsJsonAsync("/api/v1/rbac/roles",
            new CreateRbacRole("ADMIN","Spoof Admin"));
        Assert.Equal(HttpStatusCode.BadRequest,forbiddenSystem.StatusCode);

        using var configured=await adminClient.PutAsJsonAsync("/api/v1/rbac/roles/TEACHER/permissions",
            new SetRbacPermissions(new[]{"hrms.read","ams.read"}));
        Assert.Equal(HttpStatusCode.NoContent,configured.StatusCode);

        using var crossTenant=await adminClient.PostAsJsonAsync("/api/v1/rbac/assignments",
            new SetRbacAssignment(foreignUser.ToString("D"),"TEACHER"));
        Assert.Equal(HttpStatusCode.NotFound,crossTenant.StatusCode);

        using var assigned=await adminClient.PostAsJsonAsync("/api/v1/rbac/assignments",
            new SetRbacAssignment(member.ToString("D"),"TEACHER"));
        Assert.Equal(HttpStatusCode.NoContent,assigned.StatusCode);

        using var revokedOldToken=await memberClient.GetAsync(
            "/api/v1/identity/permissions/check?permission=hrms.read&campusId="+campus.ToString("D"));
        Assert.Equal(HttpStatusCode.Unauthorized,revokedOldToken.StatusCode);

        using var refreshed=await Login(member);
        using var global=await refreshed.GetAsync("/api/v1/identity/permissions/check?permission=hrms.read");
        Assert.Equal(HttpStatusCode.OK,global.StatusCode);
        var globalCheck=await global.Content.ReadFromJsonAsync<PermissionResult>();
        Assert.True(globalCheck?.Allowed);

        using var noCampus=await refreshed.GetAsync(
            "/api/v1/hrms/employees?campusId="+campus.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden,noCampus.StatusCode);
        using var grantedCampus=await adminClient.PostAsJsonAsync("/api/v1/identity/campus-access",
            new CampusGrantRequest(member.ToString("D"),campus));
        Assert.Equal(HttpStatusCode.NoContent,grantedCampus.StatusCode);
        using var allowed=await refreshed.GetAsync("/api/v1/hrms/employees?campusId="+campus.ToString("D"));
        Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        using var foreignRead=await refreshed.GetAsync("/api/v1/hrms/employees?campusId="+otherCampus.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden,foreignRead.StatusCode);
        using var prohibitedWrite=await refreshed.PostAsJsonAsync("/api/v1/hrms/employees",new {campusId=campus});
        Assert.Equal(HttpStatusCode.Forbidden,prohibitedWrite.StatusCode);

        using var remove=await adminClient.DeleteAsync(
            "/api/v1/rbac/assignments/"+member.ToString("D")+"/TEACHER");
        Assert.Equal(HttpStatusCode.NoContent,remove.StatusCode);
        using var oldToken=await refreshed.GetAsync("/api/v1/rbac/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized,oldToken.StatusCode);
        using var after=await Login(member);
        using var noPrivilege=await after.GetAsync("/api/v1/hrms/employees?campusId="+campus.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden,noPrivilege.StatusCode);
    }

    [Fact]
    public async Task AnonymousManagementIsDeniedAndRoleListsAreTenantScoped()
    {
        if(factory is null)return;
        using var anon=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var result=await anon.GetAsync("/api/v1/rbac/roles");
        Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
        using var memberClient=await Login(foreignUser);
        using var unauthorized=await memberClient.PostAsJsonAsync("/api/v1/rbac/roles",
            new CreateRbacRole("STAFF","Staff"));
        Assert.Equal(HttpStatusCode.Forbidden,unauthorized.StatusCode);
    }

    private sealed record PermissionResult(bool Allowed);
    public Task DisposeAsync(){factory?.Dispose();return Task.CompletedTask;}
}
