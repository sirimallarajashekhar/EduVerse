using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Portal.Tests;
public sealed class ExamPageTests:IClassFixture<WebApplicationFactory<Program>>
{
 private readonly HttpClient client;
 public ExamPageTests(WebApplicationFactory<Program> factory)=>
  client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
 [Fact]
 public async Task AnonymousExamAdminRedirectsToLogin()
 {
  using var response=await client.GetAsync("/admin/exams");
  Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
  Assert.Contains("/account/login",response.Headers.Location?.ToString());
 }
 [Fact]
 public async Task AnonymousExamBffCannotReadGrades()
 {
  using var response=await client.GetAsync("/bff/exams/schemes?campusId="+Guid.NewGuid());
  Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
 }
 [Fact]
 public async Task AnonymousMarkPostDoesNotBypassAuthenticationOrAntiforgery()
 {
  using var response=await client.PostAsync("/bff/exams/marks",
   new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
  Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest);
 }
}
