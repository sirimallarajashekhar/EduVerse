using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Ams.Tests;
public sealed class Phase13AmsWebTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public Phase13AmsWebTests(WebApplicationFactory<Program> factory)=>
  client=factory.WithWebHostBuilder(b=>b.UseSetting("environment","Testing"))
   .CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousAmsPageRedirectsToLogin()
 {
  using var response=await client.GetAsync("/ams");
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
  Assert.Contains("/account/login",response.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousAmsBffCannotReadInventory()
 {
  using var response=await client.GetAsync("/ams/bff/stock?campusId="+Guid.NewGuid());
  Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
 }
 [Fact]
 public async Task AnonymousStockMovementPostCannotBypassAuthOrCsrf()
 {
  using var response=await client.PostAsync("/ams/bff/stock/movements",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
 }
 [Fact]
 public async Task AmsHealthEndpointIsIndependentlyDeployed()
 {
  using var response=await client.GetAsync("/ams/health/live");
  Assert.Equal(HttpStatusCode.OK,response.StatusCode);
 }
}
