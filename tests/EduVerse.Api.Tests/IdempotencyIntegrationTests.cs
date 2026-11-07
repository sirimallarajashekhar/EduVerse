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

/// <summary>Runs with CI MySQL 8, verifies transactional replay and outbox effects.</summary>
public sealed class IdempotencyIntegrationTests : IAsyncLifetime
{
    private readonly string? connection = Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();
    private readonly string tenantSlug = "idem-" + Guid.NewGuid().ToString("N");
    private readonly string email = Guid.NewGuid().ToString("N") + "@example.org";
    private const string Password = "Test-Idem-Password-2026!";
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(connection)) return;
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync();
        await using (var tenant = new MySqlCommand("""
            INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Idempotency Test School','School')
            """, db))
        {
            tenant.Parameters.AddWithValue("@id", tenantId.ToString("D"));
            tenant.Parameters.AddWithValue("@slug", tenantSlug);
            await tenant.ExecuteNonQueryAsync();
        }
        await using (var account = new MySqlCommand("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES (@uid,@email,@normal,'Idempotency Test Administrator',@hash)
            """, db))
        {
            account.Parameters.AddWithValue("@uid", userId.ToString("D"));
            account.Parameters.AddWithValue("@email", email);
            account.Parameters.AddWithValue("@normal", email.ToUpperInvariant());
            account.Parameters.AddWithValue("@hash", new PasswordHasher<object>().HashPassword(new object(), Password));
            await account.ExecuteNonQueryAsync();
        }
        await using (var member = new MySqlCommand("""
            INSERT INTO tenant_memberships(tenant_id,subject_id,role_code)
            VALUES(@tid,@uid,'ADMIN')
            """, db))
        {
            member.Parameters.AddWithValue("@tid", tenantId.ToString("D"));
            member.Parameters.AddWithValue("@uid", userId.ToString("D"));
            await member.ExecuteNonQueryAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            web.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:EduVerse"] = connection })));
    }

    [Fact]
    public async Task RepeatedRequest_CreatesSingleCampusAndSingleOutboxEvent()
    {
        if (factory is null || connection is null) return;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var login = await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(tenantSlug, email, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var ticket = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket.AccessToken);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "campus-" + Guid.NewGuid().ToString("N"));

        async Task<HttpResponseMessage> Post(string name) =>
            await client.PostAsJsonAsync("/api/v1/institutions/campuses/idempotent",
                new { code = "NORTH", name });

        using var first = await Post("North Campus");
        using var replay = await Post("North Campus");
        using var conflict = await Post("Different Campus");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<CampusResult>();
        var again = await replay.Content.ReadFromJsonAsync<CampusResult>();
        Assert.NotNull(created);
        Assert.NotNull(again);
        Assert.Equal(created.Id, again.Id);
        Assert.Equal(tenantId, created.TenantId);
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync();
        await using var query = new MySqlCommand("""
            SELECT COUNT(*) FROM integration_outbox
            WHERE tenant_id=@tid AND type='campus.created'
            """, db);
        query.Parameters.AddWithValue("@tid", tenantId.ToString("D"));
        Assert.Equal(1, Convert.ToInt32(await query.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task IdempotentWrites_RequireAuthentication()
    {
        if (factory is null) return;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Idempotency-Key", "demo-authenticated-only");
        using var response = await client.PostAsJsonAsync("/api/v1/institutions/campuses/idempotent",
            new { code = "MAIN", name = "Main Campus" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public Task DisposeAsync() { factory?.Dispose(); return Task.CompletedTask; }
    private sealed record CampusResult(Guid Id, Guid TenantId, string Code, string Name, bool IsActive);
}
