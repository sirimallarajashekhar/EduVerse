using System.Net;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
namespace EduVerse.Api.Tests;
public sealed class FoundationApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public FoundationApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }
    [Fact] public async Task Info_ReturnsVersionedContract()
    {
        var response = await _client.GetAsync("/api/v1/system/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();
        Assert.NotNull(payload);
        Assert.Equal("EduVerse", payload.Application);
        Assert.Equal("v1", payload.ApiVersion);
    }
    [Fact] public async Task Liveness_RespondsSuccessfully()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
