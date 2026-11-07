using EduVerse.Contracts;
using Xunit;
namespace EduVerse.Contracts.Tests;
public sealed class SystemInfoResponseTests
{
    [Fact] public void StoresVersionedContractValues()
    {
        var now = DateTimeOffset.UtcNow;
        var response = new SystemInfoResponse("EduVerse", "v1", now);
        Assert.Equal("EduVerse", response.Application);
        Assert.Equal("v1", response.ApiVersion);
        Assert.Equal(now, response.ServerTimeUtc);
    }
}
