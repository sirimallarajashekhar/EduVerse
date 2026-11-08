using EduVerse.Api.Modules;
using Xunit;

namespace EduVerse.Api.Tests;
public sealed class TenantModuleCatalogTests
{
    [Fact]
    public void SchoolPilotShowsCoreAcademicsAndLocksAdvancedFeatures()
    {
        var modules=TenantModuleCatalog.ForAdmin("School",new[]{"core","academics"});
        Assert.DoesNotContain(modules,x=>x.Id=="college");
        Assert.Contains(modules,x=>x.Id=="institution"&&x.Enabled);
        Assert.Contains(modules,x=>x.Id=="students"&&x.Enabled);
        Assert.Contains(modules,x=>x.Id=="attendance"&&x.Enabled);
        Assert.Contains(modules,x=>x.Id=="finance"&&!x.Enabled);
        Assert.Contains(modules,x=>x.Id=="reporting"&&!x.Enabled);
        Assert.All(modules,x=>Assert.StartsWith("/admin/",x.Route));
        Assert.Equal(modules.Count,modules.Select(x=>x.Route).Distinct().Count());
    }

    [Fact]
    public void CollegeCatalogIncludesCollegeButRequiresEntitlement()
    {
        var modules=TenantModuleCatalog.ForAdmin("College",new[]{"core","academics"});
        Assert.Contains(modules,x=>x.Id=="college"&&!x.Enabled);
        Assert.Contains(modules,x=>x.Id=="exams"&&x.Enabled);
        Assert.Equal(modules.Count,modules.Select(x=>x.Id).Distinct().Count());
    }

    [Fact]
    public void EmptyFeaturesUnlockNothing()
    {
        var modules=TenantModuleCatalog.ForAdmin("Mixed",Array.Empty<string>());
        Assert.All(modules,x=>Assert.False(x.Enabled));
        Assert.Contains(modules,x=>x.Id=="college");
    }
}
