using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using EduVerse.Application.Tenancy;
using EduVerse.Infrastructure.MySql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;

/// <summary>MySQL-backed isolation tests, executed in CI with EDUVERSE_TEST_MYSQL configured.</summary>
public sealed class TenantIsolationTests : IClassFixture<TenantFixture>
{
    private readonly TenantFixture fixture;
    public TenantIsolationTests(TenantFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task UnauthenticatedRequest_IsRejected()
    {
        using var client = fixture.CreateClient();
        using var response = await client.GetAsync("/api/v1/institutions/campuses");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NoTenantClaim_IsForbidden()
    {
        using var client = fixture.CreateClient(subject: "admin-a");
        using var response = await client.GetAsync("/api/v1/institutions/current");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantA_OnlyReadsOwnCampuses_EvenWithSpoofedHeader()
    {
        if (!fixture.HasDatabase) return; // CI explicitly supplies a live MySQL test database.
        using var client = fixture.CreateClient(subject: "admin-a", tenantId: fixture.TenantA);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", fixture.TenantB.ToString());
        using var response = await client.GetAsync("/api/v1/institutions/campuses");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var campuses = await response.Content.ReadFromJsonAsync<CampusResponse[]>();
        Assert.NotNull(campuses);
        Assert.Contains(campuses, c => c.Code == "A-ONLY");
        Assert.DoesNotContain(campuses, c => c.Code == "B-ONLY");
        Assert.All(campuses, c => Assert.Equal(fixture.TenantA, c.TenantId));
    }

    [Fact]
    public async Task UnregisteredSubject_CannotAccessAnotherTenant()
    {
        if (!fixture.HasDatabase) return;
        using var client = fixture.CreateClient(subject: "admin-a", tenantId: fixture.TenantB);
        using var response = await client.GetAsync("/api/v1/institutions/current");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_CannotCreateCampus()
    {
        if (!fixture.HasDatabase) return;
        using var client = fixture.CreateClient(subject: "member-b", tenantId: fixture.TenantB);
        using var response = await client.PostAsJsonAsync("/api/v1/institutions/campuses",
            new { code = "NEW-B", name = "Unauthorized Campus" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCreateCampus_UsesClaimTenant_AndValidatesInputs()
    {
        if (!fixture.HasDatabase) return;
        using var client = fixture.CreateClient(subject: "admin-a", tenantId: fixture.TenantA);
        using var invalid = await client.PostAsJsonAsync("/api/v1/institutions/campuses",
            new { code = "invalid code!", name = "Test" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var created = await client.PostAsJsonAsync("/api/v1/institutions/campuses",
            new { code = "C-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(), name = "North Campus", tenantId = fixture.TenantB });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var campus = await created.Content.ReadFromJsonAsync<CampusResponse>();
        Assert.NotNull(campus);
        Assert.Equal(fixture.TenantA, campus.TenantId);
    }

    private sealed record CampusResponse(Guid Id, Guid TenantId, string Code, string Name, bool IsActive);
}

public sealed class TenantFixture : IAsyncLifetime
{
    public Guid TenantA { get; } = Guid.NewGuid();
    public Guid TenantB { get; } = Guid.NewGuid();
    private readonly string? connectionString = Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    public bool HasDatabase => !string.IsNullOrWhiteSpace(connectionString);

    public async Task InitializeAsync()
    {
        if (!HasDatabase) return;
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await InsertAsync(connection, "INSERT INTO tenants(id,slug,display_name,kind) VALUES (@id,@slug,@name,'School')",
            ("@id", TenantA.ToString("D")), ("@slug", $"test-a-{TenantA:N}"), ("@name", "Test School A"));
        await InsertAsync(connection, "INSERT INTO tenants(id,slug,display_name,kind) VALUES (@id,@slug,@name,'College')",
            ("@id", TenantB.ToString("D")), ("@slug", $"test-b-{TenantB:N}"), ("@name", "Test College B"));
        await InsertAsync(connection, "INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES (@tenant,@subject,'ADMIN')",
            ("@tenant", TenantA.ToString("D")), ("@subject", "admin-a"));
        await InsertAsync(connection, "INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES (@tenant,@subject,'MEMBER')",
            ("@tenant", TenantB.ToString("D")), ("@subject", "member-b"));
        await InsertAsync(connection, "INSERT INTO campuses(id,tenant_id,code,display_name) VALUES (@id,@tenant,@code,'Campus A')",
            ("@id", Guid.NewGuid().ToString("D")), ("@tenant", TenantA.ToString("D")), ("@code", "A-ONLY"));
        await InsertAsync(connection, "INSERT INTO campuses(id,tenant_id,code,display_name) VALUES (@id,@tenant,@code,'Campus B')",
            ("@id", Guid.NewGuid().ToString("D")), ("@tenant", TenantB.ToString("D")), ("@code", "B-ONLY"));
    }

    private static async Task InsertAsync(MySqlConnection connection, string sql, params (string Name, object Value)[] args)
    {
        await using var cmd = new MySqlCommand(sql, connection);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    public HttpClient CreateClient(string? subject = null, Guid? tenantId = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.ConfigureTestServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                    options.DefaultForbidScheme = "Test";
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.Replace(ServiceDescriptor.Scoped<IInstitutionRepository>(_ =>
                    new MySqlInstitutionRepository(connectionString ?? "Server=localhost;Database=unused;User ID=unused;Password=unused")));
            });
        });
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (subject is not null) client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        if (tenantId is not null) client.DefaultRequestHeaders.Add("X-Test-Identity-Tenant", tenantId.Value.ToString("D"));
        return client;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var subject = Request.Headers["X-Test-Subject"].ToString();
        if (string.IsNullOrWhiteSpace(subject))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        var tenant = Request.Headers["X-Test-Identity-Tenant"].ToString();
        if (!string.IsNullOrWhiteSpace(tenant)) claims.Add(new Claim("tenant_id", tenant));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, "Test")));
    }
}
