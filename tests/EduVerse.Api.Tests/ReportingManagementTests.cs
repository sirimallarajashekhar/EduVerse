using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Application.Reporting;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;

/// <summary>MySQL-backed security and reporting tests using only synthetic institution records.</summary>
public sealed class ReportingManagementTests : IAsyncLifetime
{
    private readonly string? cs = Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenant = Guid.NewGuid(), otherTenant = Guid.NewGuid();
    private readonly Guid campus = Guid.NewGuid(), otherCampus = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid(), otherAdmin = Guid.NewGuid(), member = Guid.NewGuid();
    private readonly string slug = "reports-" + Guid.NewGuid().ToString("N");
    private readonly string otherSlug = "reports-other-" + Guid.NewGuid().ToString("N");
    private readonly string adminEmail = Guid.NewGuid().ToString("N") + "@example.org";
    private readonly string otherEmail = Guid.NewGuid().ToString("N") + "@example.org";
    private readonly string memberEmail = Guid.NewGuid().ToString("N") + "@example.org";
    private const string Password = "Reporting-Integration-2026!";
    private WebApplicationFactory<Program>? factory;
    private static string Id(Guid id) => id.ToString("D");

    private async Task Exec(string sql, params (string Name, object Value)[] args)
    {
        await using var db = new MySqlConnection(cs!);
        await db.OpenAsync();
        await using var command = new MySqlCommand(sql, db);
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
    public async Task InitializeAsync()
    {
        if (cs is null) return;
        await Exec("""
            INSERT INTO tenants(id,slug,display_name,kind) VALUES
            (@tenant,@slug,'Report School','School'),(@other,@otherSlug,'Report College','College')
            """, ("@tenant", Id(tenant)), ("@slug", slug),
            ("@other", Id(otherTenant)), ("@otherSlug", otherSlug));
        var hash = new PasswordHasher<object>().HashPassword(new object(), Password);
        foreach (var (id, email) in new[] { (admin, adminEmail), (otherAdmin, otherEmail), (member, memberEmail) })
            await Exec("""
                INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
                VALUES(@id,@email,@normalized,'Reporting Test',@hash)
                """, ("@id", Id(id)), ("@email", email),
                ("@normalized", email.ToUpperInvariant()), ("@hash", hash));
        await Exec("""
            INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES
            (@t,@admin,'ADMIN'),(@t,@member,'MEMBER'),(@other,@otherAdmin,'ADMIN')
            """, ("@t", Id(tenant)), ("@admin", Id(admin)), ("@member", Id(member)),
            ("@other", Id(otherTenant)), ("@otherAdmin", Id(otherAdmin)));
        await Exec("""
            INSERT INTO campuses(id,tenant_id,code,display_name) VALUES
            (@campus,@t,'MAIN','School Main'),(@otherCampus,@other,'MAIN','College Main')
            """, ("@campus", Id(campus)), ("@t", Id(tenant)), ("@otherCampus", Id(otherCampus)),
            ("@other", Id(otherTenant)));
        await Exec("""
            INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES
            (@t,'STANDARD'),(@other,'PILOT')
            """, ("@t", Id(tenant)), ("@other", Id(otherTenant)));
        await Exec("""
            INSERT INTO students(id,tenant_id,campus_id,admission_number,given_name,family_name,date_of_birth)
            VALUES(@id,@t,@c,'REPORT-1','Synthetic','Student','2013-06-01')
            """, ("@id", Id(Guid.NewGuid())), ("@t", Id(tenant)), ("@c", Id(campus)));
        await Exec("""
            INSERT INTO hr_employees(id,tenant_id,campus_id,employee_number,given_name,family_name,
                                     joined_on,employee_type,status)
            VALUES(@id,@t,@c,'REPORT-HR-1','Synthetic','Staff','2026-01-01','STAFF','ACTIVE')
            """, ("@id", Id(Guid.NewGuid())), ("@t", Id(tenant)), ("@c", Id(campus)));
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:EduVerse"] = cs })));
    }
    private async Task<HttpClient> Client(int who)
    {
        var client = factory!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (tenantSlug, email) = who switch
        {
            0 => (slug, adminEmail),
            1 => (slug, memberEmail),
            _ => (otherSlug, otherEmail)
        };
        using var response = await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(tenantSlug, email, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ticket.AccessToken);
        return client;
    }
    private static string Range(Guid campusId)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-40));
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return $"campusId={campusId:D}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
    }
    [Fact]
    public async Task TenantCampusIsolationAndRoleEntitlementAreEnforced()
    {
        if (factory is null) return;
        using var adminClient = await Client(0);
        using var response = await adminClient.GetAsync("/api/v1/reporting/dashboard?" + Range(campus));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<InstitutionAnalytics>();
        Assert.NotNull(report);
        Assert.Equal(1, report.Kpis.ActiveStudents);
        Assert.Equal(1, report.Kpis.ActiveStaff);
        Assert.Equal(0m, report.Kpis.OutstandingInr);
        Assert.NotEmpty(report.Months);

        using var foreign = await adminClient.GetAsync("/api/v1/reporting/dashboard?" + Range(otherCampus));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var memberClient = await Client(1);
        using var denied = await memberClient.GetAsync("/api/v1/reporting/dashboard?" + Range(campus));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var pilotClient = await Client(2);
        using var notSubscribed = await pilotClient.GetAsync("/api/v1/reporting/dashboard?" + Range(otherCampus));
        Assert.Equal(HttpStatusCode.Forbidden, notSubscribed.StatusCode);
    }
    [Fact]
    public async Task CsvContainsOnlyFixedSchemaAndRejectsInvalidRanges()
    {
        if (factory is null) return;
        using var client = await Client(0);
        using var download = await client.GetAsync("/api/v1/reporting/monthly.csv?" + Range(campus));
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.StartsWith("text/csv", download.Content.Headers.ContentType?.ToString());
        var csv = await download.Content.ReadAsStringAsync();
        Assert.StartsWith("year,month,enrollments,payments,collected_inr", csv);
        Assert.DoesNotContain("Synthetic", csv);
        Assert.DoesNotContain("REPORT-", csv);
        using var invalid = await client.GetAsync(
            $"/api/v1/reporting/dashboard?campusId={campus:D}&from=2025-01-01&to=2026-11-01");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
    }
}
