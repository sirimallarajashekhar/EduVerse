using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
namespace EduVerse.Portal.Tests;
public sealed class CollegePageTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public CollegePageTests(WebApplicationFactory<Program> factory)=>
  client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousCollegeAdministrationRedirectsToSignIn()
 {
  using var result=await client.GetAsync("/admin/college");
  Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
  Assert.Contains("/account/login",result.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousTranscriptBffDeniesAccess()
 {
  using var result=await client.GetAsync(
   $"/bff/college/transcripts?campusId={Guid.NewGuid():D}&enrollmentId={Guid.NewGuid():D}");
  Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
 }
 [Fact]
 public async Task AnonymousGradeImportCannotBypassAuthenticationOrCsrf()
 {
  using var result=await client.PostAsync("/bff/college/grades/import",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(result.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
 }
}
