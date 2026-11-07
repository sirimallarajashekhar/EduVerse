using System.Security.Claims;
using EduVerse.Application.Tenancy;
using EduVerse.Domain;

namespace EduVerse.Api.Security;

/// <summary>Reads only authenticated identity claims; never trusts a tenant request header or body.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public bool TryGet(out TenantIdentity? identity)
    {
        identity = null;
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return false;
        var tenantClaim = user.FindFirst("tenant_id")?.Value;
        var subject = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        if (!Guid.TryParse(tenantClaim, out var tenantId) || tenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 128) return false;
        identity = new TenantIdentity(tenantId, subject);
        return true;
    }
}
