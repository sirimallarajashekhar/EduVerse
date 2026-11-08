using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;

/// <summary>Exercises the real JWT, MySQL transaction, duplicate-review guard and role separation.</summary>
public sealed class PlatformApprovalTests : IAsyncLifetime
{
    private readonly string? connection=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid operatorId=Guid.NewGuid(),tenantId=Guid.NewGuid();
    private readonly string email=Guid.NewGuid().ToString("N")+"@example.org";
    private readonly string slug="operator-"+Guid.NewGuid().ToString("N");
    private const string Password="Platform-Test-Secure-Pass-2026!";
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        if(string.IsNullOrWhiteSpace(connection))return;
        await using var db=new MySqlConnection(connection);await db.OpenAsync();
        async Task Insert(string sql,params (string,object)[] parameters)
        {
            await using var cmd=new MySqlCommand(sql,db);
            foreach(var (key,value) in parameters)cmd.Parameters.AddWithValue(key,value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Insert("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Operator School','School')",
            ("@id",tenantId.ToString("D")),("@slug",slug));
        await Insert("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES(@id,@email,@normalized,'Test Platform Operator',@hash)
            """,("@id",operatorId.ToString("D")),("@email",email),("@normalized",email.ToUpperInvariant()),
            ("@hash",new PasswordHasher<object>().HashPassword(new object(),Password)));
        await Insert("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@tid,@uid,'ADMIN')",
            ("@tid",tenantId.ToString("D")),("@uid",operatorId.ToString("D")));
        factory=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
            web.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(
                new Dictionary<string,string?>{["ConnectionStrings:EduVerse"]=connection})));
    }

    private async Task<HttpClient> OperatorClientAsync()
    {
        var client=factory!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var login=await client.PostAsJsonAsync("/api/v1/platform-auth/login",new {email,password=Password});
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var ticket=await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        Assert.Equal("PLATFORM_SUPER_ADMIN",ticket.RoleCode);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
        return client;
    }

    [Fact]
    public async Task TenantAdminCannotReview_AndOperatorMustBeProvisioned()
    {
        if(factory is null)return;
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var notProvisioned=await client.PostAsJsonAsync("/api/v1/platform-auth/login",new{email,password=Password});
        Assert.Equal(HttpStatusCode.Unauthorized,notProvisioned.StatusCode);
        using var tenantLogin=await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(slug,email,Password));
        var ticket=await tenantLogin.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
        using var forbidden=await client.GetAsync("/api/v1/platform-admin/registrations");
        Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
    }

    [Fact]
    public async Task ApprovalCreatesIsolatedTenantAndRejectsReplay()
    {
        if(factory is null||connection is null)return;
        await using(var db=new MySqlConnection(connection))
        {
            await db.OpenAsync();
            await using var grant=new MySqlCommand("INSERT IGNORE INTO platform_operators(user_id) VALUES(@id)",db);
            grant.Parameters.AddWithValue("@id",operatorId.ToString("D"));
            await grant.ExecuteNonQueryAsync();
        }
        using var client=await OperatorClientAsync();
        var requestSlug="approved-"+Guid.NewGuid().ToString("N");
        using var register=await client.PostAsJsonAsync("/api/v1/institution-registration/requests",
            new RegistrationRequest(requestSlug,"Approved School","School",Guid.NewGuid().ToString("N")+"@example.org"));
        Assert.Equal(HttpStatusCode.Accepted,register.StatusCode);
        var receipt=await register.Content.ReadFromJsonAsync<RegistrationReceipt>();
        Assert.NotNull(receipt);
        using var approved=await client.PostAsJsonAsync(
            $"/api/v1/platform-admin/registrations/{receipt.RequestId}/approve",new{reason="Verified school details"});
        Assert.Equal(HttpStatusCode.OK,approved.StatusCode);
        using var replay=await client.PostAsJsonAsync(
            $"/api/v1/platform-admin/registrations/{receipt.RequestId}/approve",new{reason="Duplicate"});
        Assert.Equal(HttpStatusCode.Conflict,replay.StatusCode);
        await using var db2=new MySqlConnection(connection);await db2.OpenAsync();
        await using var check=new MySqlCommand("""
            SELECT COUNT(*) FROM tenants t JOIN institution_profiles p ON p.tenant_id=t.id
            JOIN tenant_memberships m ON m.tenant_id=t.id AND m.role_code='ADMIN'
            JOIN institution_subscriptions s ON s.tenant_id=t.id AND s.plan_code='PILOT'
            WHERE t.slug=@slug
            """,db2);
        check.Parameters.AddWithValue("@slug",requestSlug);
        Assert.Equal(1,Convert.ToInt32(await check.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task RevokedOperatorSessionLosesAccess()
    {
        if(factory is null||connection is null)return;
        await using(var db=new MySqlConnection(connection))
        {
            await db.OpenAsync();
            await using var grant=new MySqlCommand("INSERT IGNORE INTO platform_operators(user_id) VALUES(@id)",db);
            grant.Parameters.AddWithValue("@id",operatorId.ToString("D"));
            await grant.ExecuteNonQueryAsync();
        }
        using var client=await OperatorClientAsync();
        using var logout=await client.PostAsync("/api/v1/platform-admin/logout",null);
        Assert.Equal(HttpStatusCode.NoContent,logout.StatusCode);
        using var rejected=await client.GetAsync("/api/v1/platform-admin/registrations");
        Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);
    }


    [Fact]
    public async Task RejectionDoesNotCreateTenant_AndIsAudited()
    {
        if(factory is null||connection is null)return;
        await using(var db=new MySqlConnection(connection))
        {
            await db.OpenAsync();
            await using var grant=new MySqlCommand("INSERT IGNORE INTO platform_operators(user_id) VALUES(@id)",db);
            grant.Parameters.AddWithValue("@id",operatorId.ToString("D"));
            await grant.ExecuteNonQueryAsync();
        }
        using var client=await OperatorClientAsync();
        var requestSlug="rejected-"+Guid.NewGuid().ToString("N");
        using var register=await client.PostAsJsonAsync("/api/v1/institution-registration/requests",
            new RegistrationRequest(requestSlug,"Rejected School","School",Guid.NewGuid().ToString("N")+"@example.org"));
        var receipt=await register.Content.ReadFromJsonAsync<RegistrationReceipt>();
        Assert.NotNull(receipt);
        using var reject=await client.PostAsJsonAsync(
            $"/api/v1/platform-admin/registrations/{receipt.RequestId}/reject",
            new{reason="Verification failed"});
        Assert.Equal(HttpStatusCode.OK,reject.StatusCode);
        await using var db2=new MySqlConnection(connection);await db2.OpenAsync();
        await using var command=new MySqlCommand("""
            SELECT COUNT(*) FROM institution_registration_requests r
              JOIN platform_approval_audit a ON a.request_id=r.id
            WHERE r.id=@id AND r.status='REJECTED'
              AND a.action='REJECTED' AND a.operator_id=@operator
              AND NOT EXISTS(SELECT 1 FROM tenants t WHERE t.slug=r.requested_slug)
            """,db2);
        command.Parameters.AddWithValue("@id",receipt.RequestId.ToString("D"));
        command.Parameters.AddWithValue("@operator",operatorId.ToString("D"));
        Assert.Equal(1,Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task PlatformConsoleRequiresLiveOperatorAndCanManageOtherInstitutionUsers()
    {
        if(factory is null||connection is null)return;
        // Tenant ADMIN cannot obtain platform powers by claiming a tenant role.
        using var tenantClient=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using(var signIn=await tenantClient.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(slug,email,Password)))
        {
            var login=await signIn.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(login);
            tenantClient.DefaultRequestHeaders.Authorization=
                new AuthenticationHeaderValue("Bearer",login.AccessToken);
        }
        using(var forbidden=await tenantClient.GetAsync("/api/v1/platform-control/institutions"))
            Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);

        await using(var db=new MySqlConnection(connection))
        {
            await db.OpenAsync();
            await using var cmd=new MySqlCommand("INSERT IGNORE INTO platform_operators(user_id) VALUES(@id)",db);
            cmd.Parameters.AddWithValue("@id",operatorId.ToString("D"));
            await cmd.ExecuteNonQueryAsync();
        }
        using var platform=await OperatorClientAsync();
        using(var institutions=await platform.GetAsync("/api/v1/platform-control/institutions"))
        {
            Assert.Equal(HttpStatusCode.OK,institutions.StatusCode);
            var json=await institutions.Content.ReadAsStringAsync();
            Assert.Contains(tenantId.ToString("D"),json,StringComparison.OrdinalIgnoreCase);
        }
        using(var existing=await platform.GetAsync($"/api/v1/platform-control/institutions/{tenantId:D}/users"))
        {
            Assert.Equal(HttpStatusCode.OK,existing.StatusCode);
            Assert.Contains(email,await existing.Content.ReadAsStringAsync());
        }
        // A super admin can invite an institution member without joining that tenant.
        var inviteEmail=Guid.NewGuid().ToString("N")+"@example.org";
        using(var invite=await platform.PostAsJsonAsync(
            $"/api/v1/platform-control/institutions/{tenantId:D}/users",
            new {tenantId,email=inviteEmail,displayName="Portal Member",roleCode="MEMBER"}))
        {
            Assert.Equal(HttpStatusCode.Accepted,invite.StatusCode);
            var receipt=await invite.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.False(receipt.GetProperty("activationEmailSent").GetBoolean()); // SMTP intentionally not configured in CI
        }
        Guid newMember;
        await using(var db=new MySqlConnection(connection))
        {
            await db.OpenAsync();
            await using var query=new MySqlCommand("""
                SELECT u.id FROM identity_users u
                JOIN tenant_memberships m ON m.subject_id=u.id
                WHERE m.tenant_id=@tenant AND u.email_normalized=@email
                """,db);
            query.Parameters.AddWithValue("@tenant",tenantId.ToString("D"));
            query.Parameters.AddWithValue("@email",inviteEmail.ToUpperInvariant());
            var value=await query.ExecuteScalarAsync();
            newMember=Guid.Parse(Convert.ToString(value)!);
        }
        using(var wrongTenant=await platform.PostAsJsonAsync(
            $"/api/v1/platform-control/institutions/{Guid.NewGuid():D}/users/{newMember:D}/deactivate",
            new {tenantId,reason="Mismatched tenant"}))
            Assert.Equal(HttpStatusCode.BadRequest,wrongTenant.StatusCode);
        using(var deactivated=await platform.PostAsJsonAsync(
            $"/api/v1/platform-control/institutions/{tenantId:D}/users/{newMember:D}/deactivate",
            new {tenantId,reason="Membership access closed"}))
            Assert.Equal(HttpStatusCode.NoContent,deactivated.StatusCode);
        // Last active institutional admin is protected even from platform operators.
        using(var blocked=await platform.PostAsJsonAsync(
            $"/api/v1/platform-control/institutions/{tenantId:D}/users/{operatorId:D}/deactivate",
            new {tenantId,reason="Attempt to remove last admin"}))
            Assert.Equal(HttpStatusCode.Conflict,blocked.StatusCode);
        using(var unknown=await platform.GetAsync($"/api/v1/platform-control/institutions/{Guid.NewGuid():D}/users"))
            Assert.Equal(HttpStatusCode.NotFound,unknown.StatusCode);

        using var logout=await platform.PostAsync("/api/v1/platform-admin/logout",null);
        Assert.Equal(HttpStatusCode.NoContent,logout.StatusCode);
        using var revoked=await platform.GetAsync("/api/v1/platform-control/institutions");
        Assert.Equal(HttpStatusCode.Unauthorized,revoked.StatusCode);
    }

    public Task DisposeAsync(){factory?.Dispose();return Task.CompletedTask;}
}
