using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;
public sealed class OperationsPageTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public OperationsPageTests(WebApplicationFactory<Program> factory)=>
  client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousCampusOperationsRedirectsToLogin()
 {
  using var response=await client.GetAsync("/admin/operations");
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
  Assert.Contains("/account/login",response.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousCampusBffCannotReadHostelAssignments()
 {
  using var response=await client.GetAsync("/bff/operations/hostel-allocations?campusId="+Guid.NewGuid());
  Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
 }
 [Fact]
 public async Task AnonymousWriteRequiresAuthenticationAndAntiforgery()
 {
  using var response=await client.PostAsync("/bff/operations/loans",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
 }
}
