using System.Net;
using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EduVerse.Api.Tests;

public sealed class ModuleRegistryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    public ModuleRegistryTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Registry_ProvidesUniqueAllowlistedModuleRoutes()
    {
        using var response = await _client.GetAsync("/api/v1/platform/modules");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var modules = await response.Content.ReadFromJsonAsync<ModuleRegistration[]>();
        Assert.NotNull(modules);
        Assert.Equal(2, modules.Length);
        Assert.Equal(2, modules.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(modules, module => module.Id == "ams" && module.RoutePrefix == "/ams");
        Assert.Contains(modules, module => module.Id == "hrms" && module.RoutePrefix == "/hrms");
    }
}
