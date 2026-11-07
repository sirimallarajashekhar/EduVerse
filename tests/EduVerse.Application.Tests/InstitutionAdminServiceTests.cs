using EduVerse.Application.Administration;
using EduVerse.Application.Tenancy;
using EduVerse.Contracts;
using EduVerse.Domain;
using Xunit;

namespace EduVerse.Application.Tests;

public sealed class InstitutionAdminServiceTests
{
    private readonly Guid tenant = Guid.NewGuid();

    [Fact]
    public async Task RegistrationRejectsUnknownKindAndMalformedSlug()
    {
        var service = Build(admin:true);
        await Assert.ThrowsAsync<ArgumentException>(() => service.RequestRegistrationAsync(
            new RegistrationRequest("wrong slug","Demo School","School","contact@example.org"),default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.RequestRegistrationAsync(
            new RegistrationRequest("demo-school","Demo School","Unknown","contact@example.org"),default));
    }

    [Fact]
    public async Task MemberCannotModifySettingsOrCreateResources()
    {
        var service = Build(admin:false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.SaveSettingAsync(new InstitutionSetting("timezone","Asia/Kolkata"),default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateResourceAsync("departments",new ResourceCreate(Guid.NewGuid(),"SCI","Science"),default));
    }

    [Fact]
    public async Task RejectsUnsupportedSettingsAndInvalidResourceTypes()
    {
        var service=Build(admin:true);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SaveSettingAsync(new InstitutionSetting("connection_string","x"),default));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ResourcesAsync("users",null,default));
    }

    private InstitutionAdminService Build(bool admin)
    {
        var ctx=new FakeContext(new TenantIdentity(tenant,"admin"));
        return new InstitutionAdminService(new FakeStore(admin),new FakeInstitutions(),ctx);
    }
    private sealed class FakeContext(TenantIdentity tenant) : ITenantContext
    {
        public bool TryGet(out TenantIdentity? identity) {identity=tenant;return true;}
    }
    private sealed class FakeInstitutions : IInstitutionRepository
    {
        public Task<Institution?> GetAccessibleInstitutionAsync(TenantIdentity identity,CancellationToken ct)=>
            Task.FromResult<Institution?>(new Institution(identity.TenantId,"demo","Demo School","School",true));
        public Task<IReadOnlyList<Campus>> ListCampusesAsync(TenantIdentity identity,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<Campus>>(Array.Empty<Campus>());
        public Task<bool> IsTenantAdminAsync(TenantIdentity identity,CancellationToken ct)=>Task.FromResult(true);
        public Task<Campus> CreateCampusAsync(Guid id,string code,string name,CancellationToken ct)=>
            Task.FromResult(new Campus(Guid.NewGuid(),id,code,name,true));
    }
    private sealed class FakeStore(bool admin):IInstitutionAdminStore
    {
        public Task<Guid> SubmitRegistrationAsync(RegistrationRequest request,CancellationToken ct)=>Task.FromResult(Guid.NewGuid());
        public Task<bool> IsTenantAdminAsync(TenantIdentity identity,CancellationToken ct)=>Task.FromResult(admin);
        public Task<InstitutionProfile?> ProfileAsync(Guid id,CancellationToken ct)=>Task.FromResult<InstitutionProfile?>(null);
        public Task SaveProfileAsync(Guid id,ProfileUpdate request,CancellationToken ct)=>Task.CompletedTask;
        public Task MarkReadyAsync(Guid id,CancellationToken ct)=>Task.CompletedTask;
        public Task<IReadOnlyList<CampusResource>> ResourcesAsync(Guid id,Guid? campus,string type,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<CampusResource>>(Array.Empty<CampusResource>());
        public Task<CampusResource?> CreateResourceAsync(Guid id,ResourceCreate request,string type,CancellationToken ct)=>
            Task.FromResult<CampusResource?>(null);
        public Task<IReadOnlyList<InstitutionSetting>> SettingsAsync(Guid id,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<InstitutionSetting>>(Array.Empty<InstitutionSetting>());
        public Task SaveSettingAsync(Guid id,string key,string value,CancellationToken ct)=>Task.CompletedTask;
        public Task<IReadOnlyList<SubscriptionPlan>> PlansAsync(CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<SubscriptionPlan>>(Array.Empty<SubscriptionPlan>());
        public Task<SubscriptionStatus> SubscriptionAsync(Guid id,CancellationToken ct)=>
            Task.FromResult(new SubscriptionStatus(null,"UNASSIGNED",null,Array.Empty<string>()));
        public Task<InstitutionDashboard> DashboardAsync(Guid id,CancellationToken ct)=>
            Task.FromResult(new InstitutionDashboard(id,"Demo School","School","DRAFT",0,0,0,1,
                new SubscriptionStatus(null,"UNASSIGNED",null,Array.Empty<string>())));
    }
}
