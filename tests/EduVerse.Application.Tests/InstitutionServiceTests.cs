using EduVerse.Application.Tenancy;
using EduVerse.Domain;
using Xunit;

namespace EduVerse.Application.Tests;

public sealed class InstitutionServiceTests
{
    private readonly Guid tenant = Guid.NewGuid();

    [Fact]
    public async Task RejectsMissingTenantIdentity()
    {
        var service = new InstitutionService(new FakeRepository(), new FakeContext(null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CampusesAsync(default));
    }

    [Fact]
    public async Task RejectsInvalidCampusCodeBeforeWriting()
    {
        var repo = new FakeRepository();
        var service = new InstitutionService(repo, new FakeContext(new TenantIdentity(tenant, "admin")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateCampusAsync("bad code!", "Main Campus", default));
        Assert.False(repo.Written);
    }

    [Fact]
    public async Task RejectsNonAdminWrites()
    {
        var repo = new FakeRepository { IsAdmin = false };
        var service = new InstitutionService(repo, new FakeContext(new TenantIdentity(tenant, "reader")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateCampusAsync("MAIN", "Main Campus", default));
        Assert.False(repo.Written);
    }

    private sealed class FakeContext(TenantIdentity? identity) : ITenantContext
    {
        public bool TryGet(out TenantIdentity? value) { value = identity; return identity is not null; }
    }

    private sealed class FakeRepository : IInstitutionRepository
    {
        public bool Written { get; private set; }
        public bool IsAdmin { get; set; } = true;
        public Task<Institution?> GetAccessibleInstitutionAsync(TenantIdentity identity, CancellationToken ct) =>
            Task.FromResult<Institution?>(new Institution(identity.TenantId, "test", "Test", "School", true));
        public Task<IReadOnlyList<Campus>> ListCampusesAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Campus>>(Array.Empty<Campus>());
        public Task<bool> IsTenantAdminAsync(TenantIdentity identity, CancellationToken ct) =>
            Task.FromResult(IsAdmin);
        public Task<Campus> CreateCampusAsync(Guid tenantId, string code, string name, CancellationToken ct)
        {
            Written = true;
            return Task.FromResult(new Campus(Guid.NewGuid(), tenantId, code, name, true));
        }
    }
}
