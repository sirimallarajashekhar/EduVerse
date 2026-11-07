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

/// <summary>Real JWT + MySQL 8 tests. Security assertions use two distinct tenant/campus hierarchies.</summary>
public sealed class AcademicManagementTests : IAsyncLifetime
{
    private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenantA=Guid.NewGuid(),tenantB=Guid.NewGuid();
    private readonly Guid campusA=Guid.NewGuid(),campusB=Guid.NewGuid();
    private readonly Guid adminA=Guid.NewGuid(),memberB=Guid.NewGuid();
    private readonly string slugA="acad-a-"+Guid.NewGuid().ToString("N");
    private readonly string slugB="acad-b-"+Guid.NewGuid().ToString("N");
    private readonly string emailA=Guid.NewGuid().ToString("N")+"@example.org";
    private readonly string emailB=Guid.NewGuid().ToString("N")+"@example.org";
    private const string Password="Phase7-Secure-Password-2026!";
    private WebApplicationFactory<Program>? host;

    public async Task InitializeAsync()
    {
        if(string.IsNullOrWhiteSpace(cs))return;
        await using var db=new MySqlConnection(cs);
        await db.OpenAsync();
        async Task Insert(string sql,params (string Name,object Value)[] args)
        {
            await using var cmd=new MySqlCommand(sql,db);
            foreach(var (name,value) in args)cmd.Parameters.AddWithValue(name,value);
            await cmd.ExecuteNonQueryAsync();
        }
        await Insert("INSERT INTO tenants(id,slug,display_name,kind) VALUES (@id,@slug,'School A','School')",
            ("@id",tenantA.ToString("D")),("@slug",slugA));
        await Insert("INSERT INTO tenants(id,slug,display_name,kind) VALUES (@id,@slug,'College B','College')",
            ("@id",tenantB.ToString("D")),("@slug",slugB));
        var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
        foreach(var (id,email) in new[]{(adminA,emailA),(memberB,emailB)})
        {
            await Insert("""
                INSERT INTO identity_users(id,email,email_normalized,display_name,password_hash)
                VALUES (@id,@email,@normalized,'Academic Test User',@hash)
                """,("@id",id.ToString("D")),("@email",email),("@normalized",email.ToUpperInvariant()),("@hash",hash));
        }
        await Insert("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES (@tenant,@id,'ADMIN')",
            ("@tenant",tenantA.ToString("D")),("@id",adminA.ToString("D")));
        await Insert("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES (@tenant,@id,'MEMBER')",
            ("@tenant",tenantB.ToString("D")),("@id",memberB.ToString("D")));
        await Insert("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES (@id,@tenant,'MAIN','Main Campus')",
            ("@id",campusA.ToString("D")),("@tenant",tenantA.ToString("D")));
        await Insert("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES (@id,@tenant,'MAIN','Other Campus')",
            ("@id",campusB.ToString("D")),("@tenant",tenantB.ToString("D")));
        await Insert("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES (@id,'PILOT')",
            ("@id",tenantA.ToString("D")));
        await Insert("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES (@id,'PILOT')",
            ("@id",tenantB.ToString("D")));
        host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
            web.ConfigureAppConfiguration((_,config)=>
                config.AddInMemoryCollection(new Dictionary<string,string?>
                {["ConnectionStrings:EduVerse"]=cs})));
    }

    private async Task<HttpClient> ClientAsync(bool admin)
    {
        var client=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var login=await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(admin?slugA:slugB,admin?emailA:emailB,Password));
        Assert.Equal(HttpStatusCode.OK,login.StatusCode);
        var ticket=await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
        return client;
    }

    [Fact]
    public async Task AdminCreatesYearHierarchyFacultyAndCalendar()
    {
        if(host is null)return;
        using var client=await ClientAsync(true);
        using var yearResponse=await client.PostAsJsonAsync("/api/v1/academic/years",
            new AcademicYearCreate(campusA,"2026-27","Academic Year 2026-27",
                new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
        Assert.Equal(HttpStatusCode.Created,yearResponse.StatusCode);
        var year=await yearResponse.Content.ReadFromJsonAsync<AcademicYear>();
        Assert.NotNull(year);

        async Task<AcademicItem> Item(string kind,string code,string name,Guid? parent=null,Guid? yearId=null)
        {
            using var response=await client.PostAsJsonAsync("/api/v1/academic/catalog",
                new AcademicItemCreate(campusA,kind,code,name,parent,yearId,null,null,null));
            Assert.Equal(HttpStatusCode.Created,response.StatusCode);
            var item=await response.Content.ReadFromJsonAsync<AcademicItem>();
            Assert.NotNull(item);
            return item;
        }

        var grade=await Item("GRADE","G05","Grade Five");
        var schoolClass=await Item("CLASS","C05","Class Five",grade.Id);
        var section=await Item("SECTION","5A","Section A",schoolClass.Id,year.Id);
        var subject=await Item("SUBJECT","MATH","Mathematics",schoolClass.Id);
        var program=await Item("PROGRAM","BSC","Bachelor of Science");
        var course=await Item("COURSE","PHY101","Physics",program.Id);
        var batch=await Item("BATCH","2026B","2026 Intake",program.Id,year.Id);
        var term=await Item("TERM","T01","Term One",null,year.Id);
        var semester=await Item("SEMESTER","SEM1","Semester One",program.Id,year.Id);
        Assert.Equal("SECTION",section.Kind);
        Assert.Equal("COURSE",course.Kind);
        Assert.Equal("BATCH",batch.Kind);
        Assert.Equal("TERM",term.Kind);
        Assert.Equal("SEMESTER",semester.Kind);

        using var assignment=await client.PostAsJsonAsync("/api/v1/academic/faculty-assignments",
            new FacultyAssignmentCreate(campusA,subject.Id,year.Id,adminA));
        Assert.Equal(HttpStatusCode.Created,assignment.StatusCode);
        using var calendar=await client.PostAsJsonAsync("/api/v1/academic/calendar",
            new AcademicEventCreate(campusA,year.Id,"HOLIDAY","Founder's Day",
                new DateOnly(2026,8,15),new DateOnly(2026,8,15),null));
        Assert.Equal(HttpStatusCode.Created,calendar.StatusCode);
        using var overview=await client.GetAsync($"/api/v1/academic/overview?campusId={campusA:D}");
        var stats=await overview.Content.ReadFromJsonAsync<AcademicOverview>();
        Assert.NotNull(stats);
        Assert.Equal(1,stats.Years);
        Assert.Equal(1,stats.Subjects);
        Assert.Equal(1,stats.Assignments);
        Assert.Equal(1,stats.CalendarEvents);
    }

    [Fact]
    public async Task OtherTenantCampusAndForeignReferencesAreRejected()
    {
        if(host is null)return;
        using var client=await ClientAsync(true);
        using var foreign=await client.PostAsJsonAsync("/api/v1/academic/years",
            new AcademicYearCreate(campusB,"X01","Foreign Campus Year",
                new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
        Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);

        using var catalog=await client.PostAsJsonAsync("/api/v1/academic/catalog",
            new AcademicItemCreate(campusA,"SECTION","S01","Invalid Section",Guid.NewGuid(),
                Guid.NewGuid(),null,null,null));
        Assert.Equal(HttpStatusCode.NotFound,catalog.StatusCode);
        using var foreignList=await client.GetAsync($"/api/v1/academic/years?campusId={campusB:D}");
        Assert.Equal(HttpStatusCode.OK,foreignList.StatusCode);
        var years=await foreignList.Content.ReadFromJsonAsync<AcademicYear[]>();
        Assert.NotNull(years);
        Assert.Empty(years);
    }

    [Fact]
    public async Task MemberCannotReadOrWriteAcademicMasters()
    {
        if(host is null)return;
        using var client=await ClientAsync(false);
        using var read=await client.GetAsync($"/api/v1/academic/catalog?campusId={campusB:D}");
        Assert.Equal(HttpStatusCode.Forbidden,read.StatusCode);
        using var write=await client.PostAsJsonAsync("/api/v1/academic/years",
            new AcademicYearCreate(campusB,"2026-27","Another Year",
                new DateOnly(2026,6,1),new DateOnly(2027,5,31)));
        Assert.Equal(HttpStatusCode.Forbidden,write.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedAndBadDatesAreRejected()
    {
        if(host is null)return;
        using var anonymous=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var rejected=await anonymous.GetAsync($"/api/v1/academic/years?campusId={campusA:D}");
        Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);
        using var client=await ClientAsync(true);
        using var invalid=await client.PostAsJsonAsync("/api/v1/academic/years",
            new AcademicYearCreate(campusA,"2027-28","Broken Dates",
                new DateOnly(2028,1,1),new DateOnly(2027,1,1)));
        Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
    }

    public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
