using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduVerse.Application.Workflows;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Xunit;

namespace EduVerse.Api.Tests;

public sealed class CrossModuleWorkflowTests : IAsyncLifetime
{
    private readonly string? connectionString = Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenant = Guid.NewGuid(), campus = Guid.NewGuid(), foreignCampus = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid(), member = Guid.NewGuid(), employee = Guid.NewGuid();
    private readonly Guid purchase = Guid.NewGuid(), asset = Guid.NewGuid(), category = Guid.NewGuid();
    private readonly Guid vendor = Guid.NewGuid(), stock = Guid.NewGuid();
    private readonly string slug = "wf-" + Guid.NewGuid().ToString("N");
    private readonly string email = Guid.NewGuid().ToString("N") + "@example.org";
    private const string Password = "Workflow-Tests-Password-2026!";
    private WebApplicationFactory<Program>? host;

    private async Task Exec(string sql, params (string Name, object Value)[] args)
    {
        await using var db = new MySqlConnection(connectionString!);
        await db.OpenAsync();
        await using var command = new MySqlCommand(sql, db);
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
    private static string Id(Guid value) => value.ToString("D");
    public async Task InitializeAsync()
    {
        if (connectionString is null) return;
        await Exec("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Workflow School','School')",
            ("@id", Id(tenant)), ("@slug", slug));
        var hash = new PasswordHasher<object>().HashPassword(new object(), Password);
        await Exec("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES(@id,@email,@normalized,'Workflow Admin',@hash)
            """, ("@id", Id(admin)), ("@email", email), ("@normalized", email.ToUpperInvariant()), ("@hash", hash));
        var otherEmail = Guid.NewGuid().ToString("N") + "@example.org";
        await Exec("""
            INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
            VALUES(@id,@email,@normalized,'Workflow Member',@hash)
            """, ("@id", Id(member)), ("@email", otherEmail), ("@normalized", otherEmail.ToUpperInvariant()), ("@hash", hash));
        await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'ADMIN')",
            ("@t", Id(tenant)), ("@u", Id(admin)));
        await Exec("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@t,@u,'MEMBER')",
            ("@t", Id(tenant)), ("@u", Id(member)));
        await Exec("""
            INSERT INTO campuses(id,tenant_id,code,display_name) VALUES
            (@campus,@t,'C1','Main Campus'),(@foreign,@t,'C2','Second Campus')
            """, ("@campus", Id(campus)), ("@foreign", Id(foreignCampus)), ("@t", Id(tenant)));
        await Exec("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@t,'STANDARD')",
            ("@t", Id(tenant)));
        await Exec("""
            INSERT INTO hr_employees
             (id,tenant_id,campus_id,employee_number,given_name,family_name,joined_on,employee_type)
            VALUES(@id,@t,@c,'HR-WORKFLOW-1','Sample','Employee','2026-01-01','STAFF')
            """, ("@id", Id(employee)), ("@t", Id(tenant)), ("@c", Id(campus)));
        await Exec("""
            INSERT INTO ams_categories(id,tenant_id,campus_id,code,display_name)
            VALUES(@id,@t,@c,'IT','IT equipment')
            """, ("@id", Id(category)), ("@t", Id(tenant)), ("@c", Id(campus)));
        await Exec("""
            INSERT INTO ams_assets(id,tenant_id,campus_id,asset_code,category_id,display_name,status,employee_id)
            VALUES(@id,@t,@c,'ASSET-WF-1',@category,'Laptop','ALLOCATED',@employee)
            """, ("@id", Id(asset)), ("@t", Id(tenant)), ("@c", Id(campus)),
            ("@category", Id(category)), ("@employee", Id(employee)));
        await Exec("""
            INSERT INTO ams_vendors(id,tenant_id,campus_id,display_name)
            VALUES(@id,@t,@c,'Office supplier')
            """, ("@id", Id(vendor)), ("@t", Id(tenant)), ("@c", Id(campus)));
        await Exec("""
            INSERT INTO ams_stock_items(id,tenant_id,campus_id,sku,display_name,unit)
            VALUES(@id,@t,@c,'PAPER','Paper','PCS')
            """, ("@id", Id(stock)), ("@t", Id(tenant)), ("@c", Id(campus)));
        await Exec("""
            INSERT INTO ams_purchase_orders
             (id,tenant_id,campus_id,vendor_id,stock_item_id,ordered_quantity,unit_price,total_price,status)
            VALUES(@id,@t,@c,@vendor,@stock,2,15,30,'ORDERED')
            """, ("@id", Id(purchase)), ("@t", Id(tenant)), ("@c", Id(campus)),
            ("@vendor", Id(vendor)), ("@stock", Id(stock)));
        host = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
            web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:EduVerse"] = connectionString })));
    }
    private async Task<HttpClient> AdminClient()
    {
        var client = host!.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var login = await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(slug, email, Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(token);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
    private static Task<HttpResponseMessage> Post(HttpClient client, string route, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route)
        { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }
    [Fact]
    public async Task IdentityBindingAndClearanceRequireScopedEmployeeAndIdempotency()
    {
        if (host is null) return;
        using var client = await AdminClient();
        var input = new EmployeeWorkflowRequest(campus, employee, member);
        using var crossCampus = await Post(client, "/api/v1/workflows/employees/bind-identity",
            input with { CampusId = foreignCampus }, "cross-campus");
        Assert.Equal(HttpStatusCode.NotFound, crossCampus.StatusCode);
        using var first = await Post(client, "/api/v1/workflows/employees/bind-identity", input, "bind1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var replay = await Post(client, "/api/v1/workflows/employees/bind-identity", input, "bind1");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var result = await replay.Content.ReadFromJsonAsync<WorkflowReceipt>();
        Assert.Equal("ALREADY_APPLIED", result?.Status);
        using var changed = await Post(client, "/api/v1/workflows/employees/bind-identity",
            input with { UserId = admin }, "bind1");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var clearance = await client.GetAsync(
            $"/api/v1/workflows/employees/{employee:D}/clearance?campusId={campus:D}");
        Assert.Equal(HttpStatusCode.OK, clearance.StatusCode);
        var summary = await clearance.Content.ReadFromJsonAsync<ClearanceSummary>();
        Assert.Equal(1, summary?.OutstandingAssets);
        using var legacyExit = await client.PostAsJsonAsync(
            $"/api/v1/hrms/employees/{employee:D}/lifecycle?campusId={campus:D}",
            new HrLifecycle("EXITED", null));
        Assert.Equal(HttpStatusCode.BadRequest, legacyExit.StatusCode);
    }
    [Fact]
    public async Task PurchaseFinanceHandoffIsIdempotentAndEmitsEvent()
    {
        if (host is null) return;
        using var client = await AdminClient();
        var request = new PurchaseWorkflowRequest(campus, purchase);
        using var first = await Post(client, "/api/v1/workflows/procurement/submit-finance", request, "purchase1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var receipt = await first.Content.ReadFromJsonAsync<WorkflowReceipt>();
        Assert.Equal("PENDING_FINANCE", receipt?.Status);
        using var replay = await Post(client, "/api/v1/workflows/procurement/submit-finance", request, "purchase1");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayReceipt = await replay.Content.ReadFromJsonAsync<WorkflowReceipt>();
        Assert.Equal(receipt?.OperationId, replayReceipt?.OperationId);
        await using var db = new MySqlConnection(connectionString);
        await db.OpenAsync();
        await using var command = new MySqlCommand("""
            SELECT COUNT(*) FROM integration_outbox
            WHERE tenant_id=@t AND type='workflow.procurement.finance_requested'
            """, db);
        command.Parameters.AddWithValue("@t", Id(tenant));
        Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }
    public async Task DisposeAsync()
    {
        if (host is not null) await host.DisposeAsync();
        // CI creates an isolated test DB; rows are intentionally synthetic.
    }
}
