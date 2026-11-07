using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;
public sealed class LearningPageTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public LearningPageTests(WebApplicationFactory<Program> factory)=>
  client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousLearningAdministrationRedirectsToLogin()
 {
  using var result=await client.GetAsync("/admin/learning");
  Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
  Assert.Contains("/account/login",result.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousLearningBffCannotReadMessages()
 {
  using var result=await client.GetAsync($"/bff/learning/threads/{Guid.NewGuid():D}/messages?campusId={Guid.NewGuid():D}");
  Assert.Equal(HttpStatusCode.Unauthorized,result.StatusCode);
 }
 [Fact]
 public async Task AnonymousGradeWriteCannotBypassAuthOrCsrf()
 {
  using var result=await client.PostAsync($"/bff/learning/submissions/{Guid.NewGuid():D}/grade?campusId={Guid.NewGuid():D}",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(result.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized);
 }
}
