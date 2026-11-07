using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class AttendancePageTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public AttendancePageTests(WebApplicationFactory<Program> factory)=>
  client=factory.CreateClient(new WebApplicationFactoryClientOptions {AllowAutoRedirect=false});

 [Fact]
 public async Task AnonymousAttendancePageRedirectsToLogin()
 {
  using var response=await client.GetAsync("/admin/attendance");
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
  Assert.Contains("/account/login",response.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousAttendanceBffIsUnauthorized()
 {
  using var response=await client.GetAsync("/bff/attendance/periods?campusId="+Guid.NewGuid());
  Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
 }
 [Fact]
 public async Task AttendancePostWithoutAuthenticationOrCsrfIsRejected()
 {
  using var response=await client.PostAsync("/bff/attendance/periods",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
 }
}
