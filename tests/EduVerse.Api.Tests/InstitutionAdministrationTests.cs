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

/// <summary>Real MySQL tenant isolation and API-owned administrator authorization.</summary>
public sealed class InstitutionAdministrationTests : IAsyncLifetime
{
    private readonly string? connection = Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenantA=Guid.NewGuid(), tenantB=Guid.NewGuid();
    private readonly Guid adminId=Guid.NewGuid(),memberId=Guid.NewGuid();
    private readonly Guid campusA=Guid.NewGuid(),campusB=Guid.NewGuid();
    private readonly string slugA="school-"+Guid.NewGuid().ToString("N");
    private readonly string slugB="college-"+Guid.NewGuid().ToString("N");
    private readonly string emailA=Guid.NewGuid().ToString("N")+"@example.org";
    private readonly string emailB=Guid.NewGuid().ToString("N")+"@example.org";
    private const string Password="Phase6-Secure-Password-2026!";
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        if(string.IsNullOrWhiteSpace(connection))return;
        await using var db=new MySqlConnection(connection);
        await db.OpenAsync();
        async Task Insert(string sql,params (string Key,object Value)[] args)
        {
            await using var cmd=new MySqlCommand(sql,db);
            foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Insert("INSERT INTO tenants(id,slug,display_name,kind) VALUES (@id,@slug,'School A','School')",
            ("@id",tenantA.ToString("D")),("@slug",slugA));
        await Insert("INSERT INTO tenants(id,slug,display_name,kind) VALUES (@id,@slug,'College B','College')",
            ("@id",tenantB.ToString("D")),("@slug",slugB));
        var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
        await Insert("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES (@id,@email,@normalized,'Administrator',@hash)
            """,("@id",adminId.ToString("D")),("@email",emailA),("@normalized",emailA.ToUpperInvariant()),("@hash",hash));
        await Insert("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES (@id,@email,@normalized,'Member',@hash)
            """,("@id",memberId.ToString("D")),("@email",emailB),("@normalized",emailB.ToUpperInvariant()),("@hash",hash));
        await Insert("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES (@tid,@uid,'ADMIN')",
            ("@tid",tenantA.ToString("D")),("@uid",adminId.ToString("D")));
        await Insert("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES (@tid,@uid,'MEMBER')",
            ("@tid",tenantB.ToString("D")),("@uid",memberId.ToString("D")));
        await Insert("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES (@id,@tid,'A-MAIN','Campus A')",
            ("@id",campusA.ToString("D")),("@tid",tenantA.ToString("D")));
        await Insert("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES (@id,@tid,'B-MAIN','Campus B')",
            ("@id",campusB.ToString("D")),("@tid",tenantB.ToString("D")));
        await Insert("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES (@tid,'PILOT')",
            ("@tid",tenantA.ToString("D")));
        factory=new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            web.ConfigureAppConfiguration((_,configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string,string?>
                {["ConnectionStrings:EduVerse"]=connection})));
    }

    private async Task<HttpClient> ClientAsync(bool admin)
    {
        var client=factory!.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});
        using var response=await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(admin?slugA:slugB,admin?emailA:emailB,Password));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var token=await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token.AccessToken);
        return client;
    }

    [Fact]
    public async Task PublicRegistrationCreatesPendingRequestOnly()
    {
        if(factory is null||connection is null)return;
        using var client=factory.CreateClient();
        var slug="pending-"+Guid.NewGuid().ToString("N");
        using var response=await client.PostAsJsonAsync("/api/v1/institution-registration/requests",
            new RegistrationRequest(slug,"Requested School","School","contact@example.org"));
        Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        var receipt=await response.Content.ReadFromJsonAsync<RegistrationReceipt>();
        Assert.NotNull(receipt);
        Assert.Equal("PENDING",receipt.Status);
        await using var db=new MySqlConnection(connection);
        await db.OpenAsync();
        await using var cmd=new MySqlCommand("""
            SELECT COUNT(*) FROM tenants WHERE slug=@slug
            """,db);
        cmd.Parameters.AddWithValue("@slug",slug);
        Assert.Equal(0,Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task AdminCanManageOwnResourcesButNotAnotherTenantCampus()
    {
        if(factory is null)return;
        using var client=await ClientAsync(admin:true);
        using var create=await client.PostAsJsonAsync("/api/v1/institution-admin/resources/departments",
            new ResourceCreate(campusA,"SCI","Science Department"));
        Assert.Equal(HttpStatusCode.Created,create.StatusCode);
        var department=await create.Content.ReadFromJsonAsync<CampusResource>();
        Assert.NotNull(department);
        Assert.Equal(tenantA,department.TenantId);
        using var crossTenant=await client.PostAsJsonAsync("/api/v1/institution-admin/resources/buildings",
            new ResourceCreate(campusB,"BLK","Wrong Tenant"));
        Assert.Equal(HttpStatusCode.NotFound,crossTenant.StatusCode);

        using var listing=await client.GetAsync("/api/v1/institution-admin/resources/departments");
        Assert.Equal(HttpStatusCode.OK,listing.StatusCode);
        var items=await listing.Content.ReadFromJsonAsync<CampusResource[]>();
        Assert.NotNull(items);
        Assert.Contains(items,x=>x.Id==department.Id);
        Assert.All(items,x=>Assert.Equal(tenantA,x.TenantId));
    }

    [Fact]
    public async Task MemberCannotSeeTenantDashboardOrModifySettings()
    {
        if(factory is null)return;
        using var client=await ClientAsync(admin:false);
        using var read=await client.GetAsync("/api/v1/institution-admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden,read.StatusCode);
        using var write=await client.PutAsJsonAsync("/api/v1/institution-admin/settings",
            new InstitutionSetting("timezone","Asia/Kolkata"));
        Assert.Equal(HttpStatusCode.Forbidden,write.StatusCode);
    }

    [Fact]
    public async Task AdminProfileOnboardingAndPilotFeaturesWork()
    {
        if(factory is null)return;
        using var client=await ClientAsync(admin:true);
        using var update=await client.PutAsJsonAsync("/api/v1/institution-admin/profile",
            new ProfileUpdate("School A Legal","admin@example.org",null,"Main Road","Hyderabad",
                "Telangana","500072"));
        Assert.Equal(HttpStatusCode.NoContent,update.StatusCode);
        using var ready=await client.PostAsync("/api/v1/institution-admin/onboarding/ready",null);
        Assert.Equal(HttpStatusCode.NoContent,ready.StatusCode);
        using var dashboard=await client.GetAsync("/api/v1/institution-admin/dashboard");
        Assert.Equal(HttpStatusCode.OK,dashboard.StatusCode);
        var stats=await dashboard.Content.ReadFromJsonAsync<InstitutionDashboard>();
        Assert.NotNull(stats);
        Assert.Equal(tenantA,stats.TenantId);
        Assert.Equal("READY",stats.OnboardingStatus);
        Assert.Equal("PILOT",stats.Subscription.PlanCode);
        Assert.Contains("core",stats.Subscription.Features);
        Assert.DoesNotContain("hrms",stats.Subscription.Features);
    }

    [Fact]
    public async Task AnonymousAdministrationIsRejected()
    {
        if(factory is null)return;
        using var client=factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});
        using var response=await client.GetAsync("/api/v1/institution-admin/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    public Task DisposeAsync()
    {
        factory?.Dispose();
        return Task.CompletedTask;
    }
}
