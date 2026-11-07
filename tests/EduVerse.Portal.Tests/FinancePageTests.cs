using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;

public sealed class FinancePageTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public FinancePageTests(WebApplicationFactory<Program> factory)=>
  client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousFinancePageRedirectsToLogin()
 {
  using var response=await client.GetAsync("/admin/finance");
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
  Assert.Contains("/account/login",response.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousFinanceBffCannotRead()
 {
  using var response=await client.GetAsync("/bff/finance/dashboard?campusId="+Guid.NewGuid());
  Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
 }
 [Fact]
 public async Task AnonymousCollectionWithoutCsrfIsRejected()
 {
  using var response=await client.PostAsync("/bff/finance/payments",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
 }
}
