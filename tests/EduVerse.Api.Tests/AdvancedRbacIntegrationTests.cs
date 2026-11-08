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

    [Fact]
    public async Task ParentAndStudentOwnershipLinksNeverExposeUnlinkedRecords()
    {
        if(factory is null)return;
        using var administrator=await Login(admin);
        using var normal=await Login(member);
        using var foreignMember=await Login(foreignUser);

        using var created=await administrator.PostAsJsonAsync("/api/v1/students",
            new StudentCreate(campus,null,"Ownership","Example",new DateOnly(2014,4,5)));
        Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var student=await created.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.NotNull(student);

        var path=$"/api/v1/ownership/my/students/{student.Id:D}?campusId={campus:D}";
        using(var before=await normal.GetAsync(path))
            Assert.Equal(HttpStatusCode.NotFound,before.StatusCode);

        using(var cannotLink=await normal.PostAsJsonAsync("/api/v1/ownership/links",
            new {campusId=campus,studentId=student.Id,userId=member,relationship="SELF"}))
            Assert.Equal(HttpStatusCode.Forbidden,cannotLink.StatusCode);

        // Parent email must match the existing student_guardians record in the same campus.
        using(var invalidParent=await administrator.PostAsJsonAsync("/api/v1/ownership/links",
            new {campusId=campus,studentId=student.Id,userId=member,relationship="PARENT"}))
            Assert.Equal(HttpStatusCode.NotFound,invalidParent.StatusCode);

        using(var guardian=await administrator.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/guardians?campusId={campus:D}",
            new GuardianCreate("Parent Example","MOTHER",null,memberEmail,true)))
            Assert.Equal(HttpStatusCode.Created,guardian.StatusCode);

        using(var linked=await administrator.PostAsJsonAsync("/api/v1/ownership/links",
            new {campusId=campus,studentId=student.Id,userId=member,relationship="PARENT"}))
            Assert.Equal(HttpStatusCode.NoContent,linked.StatusCode);

        using(var accessible=await normal.GetAsync(path))
            Assert.Equal(HttpStatusCode.OK,accessible.StatusCode);
        using(var hidden=await foreignMember.GetAsync(
            $"/api/v1/ownership/my/students/{student.Id:D}?campusId={campus:D}"))
            Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
        using(var wrongCampus=await normal.GetAsync(
            $"/api/v1/ownership/my/students/{student.Id:D}?campusId={otherCampus:D}"))
            Assert.Equal(HttpStatusCode.NotFound,wrongCampus.StatusCode);
        using(var mine=await normal.GetAsync("/api/v1/ownership/my/students"))
        {
            Assert.Equal(HttpStatusCode.OK,mine.StatusCode);
            var body=await mine.Content.ReadAsStringAsync();
            Assert.Contains(student.Id.ToString("D"),body,StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task DelegatedReadsRequirePermissionAndCampusAndWritesStayAdminOnly()
    {
        if(factory is null)return;
        using var administrator=await Login(admin);
        using var normal=await Login(member);
        var url=$"/api/v1/academic/years?campusId={campus:D}";
        using(var denied=await normal.GetAsync(url))
            Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);

        using(var create=await administrator.PostAsJsonAsync("/api/v1/rbac/roles",
            new CreateRbacRole("READER","Academic Reader")))
            Assert.Equal(HttpStatusCode.NoContent,create.StatusCode);
        using(var rights=await administrator.PutAsJsonAsync("/api/v1/rbac/roles/READER/permissions",
            new SetRbacPermissions(new[]{"academic.read"})))
            Assert.Equal(HttpStatusCode.NoContent,rights.StatusCode);
        using(var assignment=await administrator.PostAsJsonAsync("/api/v1/rbac/assignments",
            new SetRbacAssignment(member.ToString("D"),"READER")))
            Assert.Equal(HttpStatusCode.NoContent,assignment.StatusCode);

        using var refreshed=await Login(member);
        using(var withoutCampus=await refreshed.GetAsync(url))
            Assert.Equal(HttpStatusCode.Forbidden,withoutCampus.StatusCode);
        using(var grant=await administrator.PostAsJsonAsync("/api/v1/identity/campus-access",
            new CampusGrantRequest(member.ToString("D"),campus)))
            Assert.Equal(HttpStatusCode.NoContent,grant.StatusCode);

        using(var allowed=await refreshed.GetAsync(url))
            Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        using(var blocked=await refreshed.GetAsync(
            $"/api/v1/academic/years?campusId={otherCampus:D}"))
            Assert.Equal(HttpStatusCode.Forbidden,blocked.StatusCode);
        using(var cannotWrite=await refreshed.PostAsJsonAsync("/api/v1/academic/years",
            new AcademicYearCreate(campus,"2026-27","Academic 2026-27",
                new DateOnly(2026,6,1),new DateOnly(2027,5,31))))
            Assert.Equal(HttpStatusCode.Forbidden,cannotWrite.StatusCode);
    }

    [Fact]
    public async Task TeacherOnlySeesStudentsInAssignedTimetableClasses()
    {
        if(factory is null)return;
        using var administrator=await Login(admin);
        using var normal=await Login(member);
        using var denied=await normal.GetAsync($"/api/v1/ownership/my/teaching/classes?campusId={campus:D}");
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);

        using(var created=await administrator.PostAsJsonAsync("/api/v1/rbac/roles",
            new CreateRbacRole("TEACHER","Assigned Teacher")))
            Assert.Equal(HttpStatusCode.NoContent,created.StatusCode);
        using(var granted=await administrator.PutAsJsonAsync("/api/v1/rbac/roles/TEACHER/permissions",
            new SetRbacPermissions(new[]{"academic.read","students.read"})))
            Assert.Equal(HttpStatusCode.NoContent,granted.StatusCode);
        using(var assign=await administrator.PostAsJsonAsync("/api/v1/rbac/assignments",
            new SetRbacAssignment(member.ToString("D"),"TEACHER")))
            Assert.Equal(HttpStatusCode.NoContent,assign.StatusCode);
        using(var campusGrant=await administrator.PostAsJsonAsync("/api/v1/identity/campus-access",
            new CampusGrantRequest(member.ToString("D"),campus)))
            Assert.Equal(HttpStatusCode.NoContent,campusGrant.StatusCode);

        var year=Guid.NewGuid();var classA=Guid.NewGuid();var classB=Guid.NewGuid();
        var subject=Guid.NewGuid();var period=Guid.NewGuid();
        await Exec("""
            INSERT INTO academic_years(id,tenant_id,campus_id,code,display_name,starts_on,ends_on)
            VALUES(@id,@tid,@campus,'YEAR26','Academic 2026-27','2026-06-01','2027-05-31')
            """,("@id",year.ToString("D")),("@tid",tenant.ToString("D")),("@campus",campus.ToString("D")));
        foreach(var (id,kind,code) in new[]{(classA,"CLASS","CLASS-A"),(classB,"CLASS","CLASS-B"),
                                              (subject,"SUBJECT","ENGLISH")})
            await Exec("""
                INSERT INTO academic_catalog(id,tenant_id,campus_id,kind,code,display_name)
                VALUES(@id,@tid,@campus,@kind,@code,'RBAC Test Catalog')
                """,("@id",id.ToString("D")),("@tid",tenant.ToString("D")),
                ("@campus",campus.ToString("D")),("@kind",kind),("@code",code));
        await Exec("""
            INSERT INTO academic_periods(id,tenant_id,campus_id,code,display_name,starts_at,ends_at)
            VALUES(@id,@tid,@campus,'P1','First period','09:00:00','10:00:00')
            """,("@id",period.ToString("D")),("@tid",tenant.ToString("D")),("@campus",campus.ToString("D")));
        await Exec("""
            INSERT INTO academic_timetable_slots(
             id,tenant_id,campus_id,academic_year_id,class_item_id,subject_id,period_id,
             teacher_subject_id,day_of_week)
            VALUES(@id,@tid,@campus,@year,@class,@subject,@period,@teacher,1)
            """,("@id",Guid.NewGuid().ToString("D")),("@tid",tenant.ToString("D")),
            ("@campus",campus.ToString("D")),("@year",year.ToString("D")),("@class",classA.ToString("D")),
            ("@subject",subject.ToString("D")),("@period",period.ToString("D")),("@teacher",member.ToString("D")));

        var ownStudent=Guid.NewGuid();var otherStudent=Guid.NewGuid();
        foreach(var (student,number,schoolClass) in new[]{
            (ownStudent,"TEACHER-A-"+Guid.NewGuid().ToString("N")[..8],classA),
            (otherStudent,"TEACHER-B-"+Guid.NewGuid().ToString("N")[..8],classB)})
        {
            await Exec("""
                INSERT INTO students(id,tenant_id,campus_id,admission_number,given_name,family_name,date_of_birth)
                VALUES(@id,@tid,@campus,@number,'Teacher','Test','2014-05-07')
                """,("@id",student.ToString("D")),("@tid",tenant.ToString("D")),
                ("@campus",campus.ToString("D")),("@number",number));
            await Exec("""
                INSERT INTO student_enrollments(
                 id,tenant_id,campus_id,student_id,academic_year_id,academic_item_id)
                VALUES(@id,@tid,@campus,@student,@year,@class)
                """,("@id",Guid.NewGuid().ToString("D")),("@tid",tenant.ToString("D")),
                ("@campus",campus.ToString("D")),("@student",student.ToString("D")),
                ("@year",year.ToString("D")),("@class",schoolClass.ToString("D")));
        }
        using var refreshed=await Login(member);
        using(var classes=await refreshed.GetAsync($"/api/v1/ownership/my/teaching/classes?campusId={campus:D}"))
        {
            Assert.Equal(HttpStatusCode.OK,classes.StatusCode);
            var json=await classes.Content.ReadAsStringAsync();
            Assert.Contains(classA.ToString("D"),json,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(classB.ToString("D"),json,StringComparison.OrdinalIgnoreCase);
        }
        using(var roster=await refreshed.GetAsync(
            $"/api/v1/ownership/my/teaching/classes/{classA:D}/students?campusId={campus:D}"))
        {
            Assert.Equal(HttpStatusCode.OK,roster.StatusCode);
            var json=await roster.Content.ReadAsStringAsync();
            Assert.Contains(ownStudent.ToString("D"),json,StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(otherStudent.ToString("D"),json,StringComparison.OrdinalIgnoreCase);
        }
        using(var wrongClass=await refreshed.GetAsync(
            $"/api/v1/ownership/my/teaching/classes/{classB:D}/students?campusId={campus:D}"))
        {
            Assert.Equal(HttpStatusCode.OK,wrongClass.StatusCode);
            Assert.Equal("[]",await wrongClass.Content.ReadAsStringAsync());
        }
    }

    private sealed record PermissionResult(bool Allowed);
    public Task DisposeAsync(){factory?.Dispose();return Task.CompletedTask;}
}
