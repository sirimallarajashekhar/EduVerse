using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class StudentPageTests:IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public StudentPageTests(WebApplicationFactory<Program> factory)=>
        client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
    [Fact]
    public async Task PublicApplicantCanSeeAntiForgeryProtectedForm()
    {
        using var result=await client.GetAsync("/apply?campusId="+Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK,result.StatusCode);
        var html=await result.Content.ReadAsStringAsync();
        Assert.Contains("Admission application",html);
        Assert.Contains("__RequestVerificationToken",html);
    }
    [Fact]
    public async Task AnonymousStudentDashboardRedirectsToLogin()
    {
        using var response=await client.GetAsync("/admin/students");
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.Contains("/account/login",response.Headers.Location?.ToString());
    }
    [Fact]
    public async Task AnonymousStudentBffCannotEnumerateRecords()
    {
        using var response=await client.GetAsync("/bff/students?campusId="+Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }
    [Fact]
    public async Task PublicAdmissionFormPostRequiresAntiForgery()
    {
        using var response=await client.PostAsync("/apply",
            new FormUrlEncodedContent(new Dictionary<string,string>{
                ["CampusId"]=Guid.NewGuid().ToString("D"),["GivenName"]="Example",
                ["FamilyName"]="Student",["DateOfBirth"]="2016-01-01"
            }));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }
}
