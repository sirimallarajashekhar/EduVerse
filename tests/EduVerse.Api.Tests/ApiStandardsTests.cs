using System.Net;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Api.Tests;

public sealed class ApiStandardsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;
    public ApiStandardsTests(WebApplicationFactory<Program> factory) =>
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task PublicModulePaging_ReturnsEnvelopeAndTraceId()
    {
        using var response = await client.GetAsync("/api/v1/platform/modules/paged?page=1&pageSize=1&sort=name");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Trace-Id"));
        var payload = await response.Content.ReadFromJsonAsync<PageResult<ModuleRegistration>>();
        Assert.NotNull(payload);
        Assert.Single(payload.Items);
        Assert.Equal(2, payload.TotalCount);
        Assert.True(payload.HasNext);
    }

    [Theory]
    [InlineData("/api/v1/platform/modules/paged?page=0")]
    [InlineData("/api/v1/platform/modules/paged?pageSize=101")]
    [InlineData("/api/v1/platform/modules/paged?sort=malicious")]
    public async Task InvalidPaging_ProducesProblemDetails(string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var result = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traceId", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnrecognizedApiVersion_DoesNotFallThrough()
    {
        using var response = await client.GetAsync("/api/v2/platform/modules/paged");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_DoesNotDependOnDatabase()
    {
        using var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_IsUnavailableWithoutConfiguredDatabase()
    {
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task HandlesConcurrentStatelessReadRequests()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(_ => client.GetAsync("/api/v1/platform/modules/paged?page=1&pageSize=2"))
            .ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // Bound the test duration; not a performance SLA.
        var responses = await Task.WhenAll(tasks).WaitAsync(timeout.Token);
        try { Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode)); }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
