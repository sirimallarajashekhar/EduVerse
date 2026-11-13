using EduVerse.Api.Modules;
using Xunit;

namespace EduVerse.Api.Tests;
public sealed class TenantModuleCatalogTests
{
    [Fact]
    public void RestrictedFeaturesShowCoreAcademicsAndLockAdvancedFeatures()
    {
        var modules=TenantModuleCatalog.ForAdmin("School",new[]{"core","academics"});
        Assert.DoesNotContain(modules,x=>x.Id=="college");
        Assert.Contains(modules,x=>x.Id=="institution"&&x.Enabled);
        Assert.Contains(modules,x=>x.Id=="students"&&x.Enabled);
        Assert.Contains(modules,x=>x.Id=="attendance"&&x.Enabled);
        Assert.Contains(modules,x=>x.Id=="finance"&&!x.Enabled);
        Assert.Contains(modules,x=>x.Id=="reporting"&&!x.Enabled);
        Assert.All(modules,x=>Assert.StartsWith("/",x.Route));
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
    [Fact]
    public void FullPilotFeaturesEnableEveryModuleForMixedInstitution()
    {
        var features=new[]{"core","academics","finance","operations","learning",
            "college","reports","workflows","ams","hrms"};
        var modules=TenantModuleCatalog.ForAdmin("Mixed",features);
        Assert.Equal(14,modules.Count);
        Assert.All(modules,x=>Assert.True(x.Enabled));
        Assert.Contains(modules,x=>x.Id=="ams" && x.Route=="/ams");
        Assert.Contains(modules,x=>x.Id=="hrms" && x.Route=="/hrms");
        Assert.Equal(modules.Count,modules.Select(x=>x.Id).Distinct().Count());
    }

    [Fact]
    public void PlatformCatalogHasNoDuplicateModuleIds()
    {
        var modules=TenantModuleCatalog.ForPlatformOverview();
        Assert.Equal(14,modules.Count);
        Assert.Equal(modules.Count,modules.Select(x=>x.Id).Distinct().Count());
    }
}
