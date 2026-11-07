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

/// <summary>Real MySQL and real JWT tests; verifies admission state, lifecycle and private document access.</summary>
public sealed class StudentManagementTests:IAsyncLifetime
{
    private readonly string? cs=Environment.GetEnvironmentVariable("EDUVERSE_TEST_MYSQL");
    private readonly Guid tenantA=Guid.NewGuid(),tenantB=Guid.NewGuid();
    private readonly Guid campusA=Guid.NewGuid(),campusB=Guid.NewGuid();
    private readonly Guid adminA=Guid.NewGuid(),memberB=Guid.NewGuid();
    private readonly string slugA="student-a-"+Guid.NewGuid().ToString("N");
    private readonly string slugB="student-b-"+Guid.NewGuid().ToString("N");
    private readonly string emailA=Guid.NewGuid().ToString("N")+"@example.org";
    private readonly string emailB=Guid.NewGuid().ToString("N")+"@example.org";
    private const string Password="Test-Student-Password-2026!";
    private WebApplicationFactory<Program>? host;

    public async Task InitializeAsync()
    {
        if(cs is null)return;
        await using var db=new MySqlConnection(cs);await db.OpenAsync();
        async Task Execute(string sql,params (string Key,object Value)[] args)
        {
            await using var cmd=new MySqlCommand(sql,db);
            foreach(var (key,value) in args)cmd.Parameters.AddWithValue(key,value);
            await cmd.ExecuteNonQueryAsync();
        }
        foreach(var (tenant,slug,kind) in new[]{(tenantA,slugA,"School"),(tenantB,slugB,"College")})
            await Execute("INSERT INTO tenants(id,slug,display_name,kind) VALUES(@id,@slug,'Student Test',@kind)",
                ("@id",tenant.ToString("D")),("@slug",slug),("@kind",kind));
        var hash=new PasswordHasher<object>().HashPassword(new object(),Password);
        foreach(var (id,email) in new[]{(adminA,emailA),(memberB,emailB)})
            await Execute("""
                INSERT INTO identity_users(id,email,email_normalized,password_hash,display_name)
                VALUES(@id,@email,@normalized,@hash,'Student Test Operator')
                """,("@id",id.ToString("D")),("@email",email),("@normalized",email.ToUpperInvariant()),("@hash",hash));
        await Execute("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@tenant,@id,'ADMIN')",
            ("@tenant",tenantA.ToString("D")),("@id",adminA.ToString("D")));
        await Execute("INSERT INTO tenant_memberships(tenant_id,subject_id,role_code) VALUES(@tenant,@id,'MEMBER')",
            ("@tenant",tenantB.ToString("D")),("@id",memberB.ToString("D")));
        foreach(var (id,tenant) in new[]{(campusA,tenantA),(campusB,tenantB)})
            await Execute("INSERT INTO campuses(id,tenant_id,code,display_name) VALUES(@id,@tenant,'MAIN','Main Campus')",
                ("@id",id.ToString("D")),("@tenant",tenant.ToString("D")));
        foreach(var tenant in new[]{tenantA,tenantB})
            await Execute("INSERT INTO institution_subscriptions(tenant_id,plan_code) VALUES(@id,'PILOT')",
                ("@id",tenant.ToString("D")));
        host=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>
            web.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(
                new Dictionary<string,string?>{{"ConnectionStrings:EduVerse",cs}})));
    }
    private async Task<HttpClient> ClientAsync(bool admin=true)
    {
        var client=host!.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var response=await client.PostAsJsonAsync("/api/v1/identity/login",
            new LoginRequest(admin?slugA:slugB,admin?emailA:emailB,Password));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var ticket=await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(ticket);
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",ticket.AccessToken);
        return client;
    }
    private static AdmissionSubmit Application(Guid campus)=>new(campus,"Ravi","Kumar",new DateOnly(2016,7,4),
        "guardian@example.org",null);

    [Fact]
    public async Task PublicApplicationAndAdminApprovalIssueUniqueAdmissions()
    {
        if(host is null)return;
        using var publicClient=host.CreateClient();
        using var first=await publicClient.PostAsJsonAsync("/api/v1/admissions/apply",Application(campusA));
        using var second=await publicClient.PostAsJsonAsync("/api/v1/admissions/apply",Application(campusA));
        Assert.Equal(HttpStatusCode.Accepted,first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,second.StatusCode);
        var receipt=await first.Content.ReadFromJsonAsync<AdmissionReceipt>();
        var other=await second.Content.ReadFromJsonAsync<AdmissionReceipt>();
        Assert.NotNull(receipt);Assert.NotNull(other);
        Assert.NotEqual(receipt.ApplicationNumber,other.ApplicationNumber);
        using var admin=await ClientAsync();
        using var decision=await admin.PostAsJsonAsync(
            $"/api/v1/students/applications/{receipt.RequestId:D}/decision?campusId={campusA:D}",
            new AdmissionDecision("APPROVED"));
        Assert.Equal(HttpStatusCode.NoContent,decision.StatusCode);
        using var repeat=await admin.PostAsJsonAsync(
            $"/api/v1/students/applications/{receipt.RequestId:D}/decision?campusId={campusA:D}",
            new AdmissionDecision("REJECTED"));
        Assert.Equal(HttpStatusCode.NotFound,repeat.StatusCode);
        using var register=await admin.PostAsJsonAsync("/api/v1/students",
            new StudentCreate(campusA,receipt.RequestId,"Ravi","Kumar",new DateOnly(2016,7,4)));
        Assert.Equal(HttpStatusCode.Created,register.StatusCode);
        var student=await register.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.NotNull(student);
        Assert.StartsWith("EV-",student.AdmissionNumber);
        using var duplicate=await admin.PostAsJsonAsync("/api/v1/students",
            new StudentCreate(campusA,receipt.RequestId,"Ravi","Kumar",new DateOnly(2016,7,4)));
        Assert.Equal(HttpStatusCode.NotFound,duplicate.StatusCode);
    }

    [Fact]
    public async Task TenantScopeIsEnforcedForAdmissionsAndStudentWrites()
    {
        if(host is null)return;
        using var client=await ClientAsync();
        using var foreign=await client.PostAsJsonAsync("/api/v1/students/applications",Application(campusB));
        Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode);
        using var foreignListing=await client.GetAsync($"/api/v1/students?campusId={campusB:D}");
        Assert.Equal(HttpStatusCode.OK,foreignListing.StatusCode);
        var list=await foreignListing.Content.ReadFromJsonAsync<StudentRecord[]>();
        Assert.NotNull(list);Assert.Empty(list);
        using var other=await ClientAsync(false);
        using var blocked=await other.GetAsync($"/api/v1/students?campusId={campusB:D}");
        Assert.Equal(HttpStatusCode.Forbidden,blocked.StatusCode);
    }

    [Fact]
    public async Task StudentGuardiansEnrollmentDocumentsAndWithdrawalAreTenantBound()
    {
        if(host is null)return;
        using var client=await ClientAsync();
        var dob=new DateOnly(2015,1,5);
        using var registered=await client.PostAsJsonAsync("/api/v1/students",
            new StudentCreate(campusA,null,"Anita","Rao",dob));
        Assert.Equal(HttpStatusCode.Created,registered.StatusCode);
        var student=await registered.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.NotNull(student);
        using var guardian=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/guardians?campusId={campusA:D}",
            new GuardianCreate("Lakshmi Rao","MOTHER","9876543210",null,true));
        Assert.Equal(HttpStatusCode.Created,guardian.StatusCode);
        using var year=await client.PostAsJsonAsync("/api/v1/academic/years",
            new AcademicYearCreate(campusA,"2030-31","Academic Year 2030",
                new DateOnly(2030,6,1),new DateOnly(2031,5,31)));
        Assert.Equal(HttpStatusCode.Created,year.StatusCode);
        var academicYear=await year.Content.ReadFromJsonAsync<AcademicYear>();
        Assert.NotNull(academicYear);
        using var grade=await client.PostAsJsonAsync("/api/v1/academic/catalog",
            new AcademicItemCreate(campusA,"GRADE","G06","Grade Six",null,null,null,null,null));
        Assert.Equal(HttpStatusCode.Created,grade.StatusCode);
        var item=await grade.Content.ReadFromJsonAsync<AcademicItem>();
        Assert.NotNull(item);
        using var enrollment=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/enrollments?campusId={campusA:D}",
            new EnrollmentCreate(academicYear.Id,item.Id));
        Assert.Equal(HttpStatusCode.Created,enrollment.StatusCode);
        using var invalidEnrollment=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/enrollments?campusId={campusB:D}",
            new EnrollmentCreate(academicYear.Id,item.Id));
        Assert.Equal(HttpStatusCode.NotFound,invalidEnrollment.StatusCode);
        using var invalidFile=new MultipartFormDataContent();
        var bytes=new byte[]{137,80,78,71,13,10,26,10,0,0,0,0};
        var file=new ByteArrayContent(bytes);
        file.Headers.ContentType=new MediaTypeHeaderValue("image/png");
        invalidFile.Add(file,"file","certificate.png");
        using var upload=await client.PostAsync(
            $"/api/v1/students/{student.Id:D}/documents?campusId={campusA:D}&category=IDENTITY",invalidFile);
        Assert.Equal(HttpStatusCode.Created,upload.StatusCode);
        var doc=await upload.Content.ReadFromJsonAsync<DocumentRecord>();
        Assert.NotNull(doc);
        using var forbidden=await client.GetAsync(
            $"/api/v1/students/{student.Id:D}/documents/{doc.Id:D}?campusId={campusB:D}");
        Assert.Equal(HttpStatusCode.NotFound,forbidden.StatusCode);
        using var download=await client.GetAsync(
            $"/api/v1/students/{student.Id:D}/documents/{doc.Id:D}?campusId={campusA:D}");
        Assert.Equal(HttpStatusCode.OK,download.StatusCode);
        Assert.Equal(bytes,await download.Content.ReadAsByteArrayAsync());
        using var verify=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/documents/{doc.Id:D}/review?campusId={campusA:D}",
            new DocumentReview("VERIFIED"));
        Assert.Equal(HttpStatusCode.NoContent,verify.StatusCode);
        using var withdraw=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/lifecycle?campusId={campusA:D}",
            new LifecycleCommand("WITHDRAW",null,null,null,"Family request"));
        Assert.Equal(HttpStatusCode.NoContent,withdraw.StatusCode);
        using var after=await client.GetAsync($"/api/v1/students/{student.Id:D}?campusId={campusA:D}");
        var inactive=await after.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.Equal("WITHDRAWN",inactive?.Status);
    }

    [Fact]
    public async Task PromotionCreatesNewYearEnrollmentAndPreservesPriorHistory()
    {
        if(host is null)return;
        using var client=await ClientAsync();
        using var registered=await client.PostAsJsonAsync("/api/v1/students",
            new StudentCreate(campusA,null,"Nisha","Rao",new DateOnly(2014,4,2)));
        var student=await registered.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.NotNull(student);
        async Task<AcademicYear> Year(string code,int first)
        {
            using var response=await client.PostAsJsonAsync("/api/v1/academic/years",
                new AcademicYearCreate(campusA,code,"Academic "+code,
                    new DateOnly(first,6,1),new DateOnly(first+1,5,31)));
            Assert.Equal(HttpStatusCode.Created,response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<AcademicYear>())!;
        }
        var current=await Year("2028-29",2028);
        var next=await Year("2029-30",2029);
        using var oldClass=await client.PostAsJsonAsync("/api/v1/academic/catalog",
            new AcademicItemCreate(campusA,"GRADE","G07","Grade Seven",null,null,null,null,null));
        var grade7=await oldClass.Content.ReadFromJsonAsync<AcademicItem>();
        Assert.NotNull(grade7);
        using var newClass=await client.PostAsJsonAsync("/api/v1/academic/catalog",
            new AcademicItemCreate(campusA,"GRADE","G08","Grade Eight",null,null,null,null,null));
        var grade8=await newClass.Content.ReadFromJsonAsync<AcademicItem>();
        Assert.NotNull(grade8);
        using var first=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/enrollments?campusId={campusA:D}",
            new EnrollmentCreate(current.Id,grade7.Id));
        Assert.Equal(HttpStatusCode.Created,first.StatusCode);
        using var promoted=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/lifecycle?campusId={campusA:D}",
            new LifecycleCommand("PROMOTE",null,next.Id,grade8.Id,"Moved to next grade"));
        Assert.Equal(HttpStatusCode.NoContent,promoted.StatusCode);
        using var history=await client.GetAsync(
            $"/api/v1/students/{student.Id:D}/enrollments?campusId={campusA:D}");
        var enrollments=await history.Content.ReadFromJsonAsync<EnrollmentRecord[]>();
        Assert.NotNull(enrollments);
        Assert.Equal(2,enrollments.Length);
        Assert.Contains(enrollments,x=>x.AcademicYearId==current.Id&&x.Status=="PROMOTED");
        Assert.Contains(enrollments,x=>x.AcademicYearId==next.Id&&x.Status=="ACTIVE");
        using var duplicate=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/lifecycle?campusId={campusA:D}",
            new LifecycleCommand("PROMOTE",null,next.Id,grade8.Id,null));
        Assert.Equal(HttpStatusCode.Conflict,duplicate.StatusCode);
    }

    [Fact]
    public async Task TransferCannotTargetForeignTenantCampus()
    {
        if(host is null)return;
        using var client=await ClientAsync();
        using var registered=await client.PostAsJsonAsync("/api/v1/students",
            new StudentCreate(campusA,null,"Arun","Kumar",new DateOnly(2015,6,6)));
        var student=await registered.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.NotNull(student);
        using var forbidden=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/lifecycle?campusId={campusA:D}",
            new LifecycleCommand("TRANSFER",campusB,null,null,"Family relocation"));
        Assert.Equal(HttpStatusCode.NotFound,forbidden.StatusCode);
        using var record=await client.GetAsync(
            $"/api/v1/students/{student.Id:D}?campusId={campusA:D}");
        var stillActive=await record.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.Equal("ACTIVE",stillActive?.Status);

        var destination=Guid.NewGuid();
        await using(var db=new MySqlConnection(cs))
        {
            await db.OpenAsync();
            await using var command=new MySqlCommand("""
                INSERT INTO campuses(id,tenant_id,code,display_name)
                VALUES(@id,@tenant,'TRANSFER-DEST','Destination Campus')
                """,db);
            command.Parameters.AddWithValue("@id",destination.ToString("D"));
            command.Parameters.AddWithValue("@tenant",tenantA.ToString("D"));
            await command.ExecuteNonQueryAsync();
        }
        using var transferred=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/lifecycle?campusId={campusA:D}",
            new LifecycleCommand("TRANSFER",destination,null,null,"Approved move"));
        Assert.Equal(HttpStatusCode.NoContent,transferred.StatusCode);
        using var after=await client.GetAsync(
            $"/api/v1/students/{student.Id:D}?campusId={campusA:D}");
        var completed=await after.Content.ReadFromJsonAsync<StudentRecord>();
        Assert.Equal("TRANSFERRED",completed?.Status);
        using var cannotTransferAgain=await client.PostAsJsonAsync(
            $"/api/v1/students/{student.Id:D}/lifecycle?campusId={campusA:D}",
            new LifecycleCommand("TRANSFER",destination,null,null,null));
        Assert.Equal(HttpStatusCode.NotFound,cannotTransferAgain.StatusCode);
    }

    [Fact]
    public async Task AnonymousAccessToPrivateStudentListIsRejected()
    {
        if(host is null)return;
        using var client=host.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        using var result=await client.GetAsync($"/api/v1/students?campusId={campusA:D}");
        Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
    }
    public Task DisposeAsync(){host?.Dispose();return Task.CompletedTask;}
}
