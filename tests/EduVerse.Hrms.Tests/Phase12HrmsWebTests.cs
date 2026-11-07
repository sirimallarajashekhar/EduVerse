using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Hrms.Tests;
public sealed class Phase12HrmsWebTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public Phase12HrmsWebTests(WebApplicationFactory<Program> factory)=>
  client=factory.WithWebHostBuilder(b=>b.UseSetting("environment","Testing"))
   .CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousHrmsPageRequiresLogin()
 {
  using var result=await client.GetAsync("/hrms");
  Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
  Assert.Contains("/account/login",result.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousHrmsBffCannotReadEmployees()
 {
  using var result=await client.GetAsync("/hrms/bff/employees?campusId="+Guid.NewGuid());
  Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
 }
 [Fact]
 public async Task AnonymousCannotPostLeaveDecision()
 {
  using var result=await client.PostAsync("/hrms/bff/leaves/"+Guid.NewGuid()+"/decision?campusId="+Guid.NewGuid(),
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
 }
 [Fact]
 public async Task HrmsLiveHealthCheckWorksIndependently()
 {
  using var result=await client.GetAsync("/hrms/health/live");
  Assert.Equal(HttpStatusCode.OK,result.StatusCode);
 }
}
