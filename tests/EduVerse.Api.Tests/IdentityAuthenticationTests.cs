using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;

/// <summary>Real JWT and real MySQL integration. No mocked authentication handler.</summary>
public sealed class IdentityAuthenticationTests : IAsyncLifetime
{
    private readonly string? connection = Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid userId = Guid.NewGuid();
    private readonly string tenantSlug = "auth-" + Guid.NewGuid().ToString("N");
    private readonly string email = Guid.NewGuid().ToString("N") + "@example.org";
    private const string Password = "Test-Secure-Password-2026!";
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(connection)) return;
        await using var db = new MySqlConnection(connection);
        await db.OpenAsync();
        await using (var t = new MySqlCommand("""
            INSERT INTO tenants(id,slug,display_name,kind)
            VALUES (@tid,@slug,'Identity Test School','School')
            """, db))
        {
            t.Parameters.AddWithValue("@tid", tenantId.ToString("D"));
            t.Parameters.AddWithValue("@slug", tenantSlug);
            await t.ExecuteNonQueryAsync();
        }
        await using (var u = new MySqlCommand("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES (@uid,@email,@normalized,'Identity Test User',@hash)
            """, db))
        {
            u.Parameters.AddWithValue("@uid", userId.ToString("D"));
            u.Parameters.AddWithValue("@email", email);
            u.Parameters.AddWithValue("@normalized", email.ToUpperInvariant());
            u.Parameters.AddWithValue("@hash", new PasswordHasher<object>().HashPassword(new object(), Password));
            await u.ExecuteNonQueryAsync();
        }
        await using (var m = new MySqlCommand("""
            INSERT INTO tenant_memberships(tenant_id,subject_id,role_code)
            VALUES (@tid,@uid,'ADMIN')
            """, db))
        {
            m.Parameters.AddWithValue("@tid", tenantId.ToString("D"));
            m.Parameters.AddWithValue("@uid", userId.ToString("D"));
            await m.ExecuteNonQueryAsync();
        }
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            web.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EduVerse"] = connection
                })));
    }

    [Fact]
    public async Task Login_SessionAndRevocation_FollowTheSameValidatedIdentity()
    {
        if (factory is null) return;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var signIn = await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(tenantSlug, email, Password));
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        var ticket = await signIn.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        Assert.Equal(tenantId, ticket.TenantId);
        Assert.Equal(userId.ToString("D"), ticket.SubjectId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ticket.AccessToken);

        using var valid = await client.GetAsync("/api/v1/identity/session");
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        using var profile = await client.GetAsync("/api/v1/identity/me");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var logout = await client.PostAsync("/api/v1/identity/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await client.GetAsync("/api/v1/identity/session");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task InvalidPasswordOrInstitution_DoesNotIssueTokens()
    {
        if (factory is null) return;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var wrongPassword = await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(tenantSlug, email, "incorrect-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        using var wrongTenant = await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest("other-institution", email, Password));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongTenant.StatusCode);
    }

    public Task DisposeAsync()
    {
        factory?.Dispose();
        return Task.CompletedTask;
    }
}
