using System.Security.Cryptography;
using System.Text;
using EduVerse.Application.Tenancy;
using EduVerse.Domain;

namespace EduVerse.Application.Workflows;

public sealed record EmployeeWorkflowRequest(Guid CampusId, Guid EmployeeId, Guid? UserId);
public sealed record PurchaseWorkflowRequest(Guid CampusId, Guid PurchaseId);
public sealed record WorkflowReceipt(Guid OperationId, string Status);
public sealed record ClearanceSummary(Guid EmployeeId, int OutstandingAssets);
public sealed class WorkflowConflictException(string message) : Exception(message);
public sealed class WorkflowScopeException : Exception;

public interface ICrossModuleStore
{
    Task<bool> IsAdminAsync(TenantIdentity actor, CancellationToken ct);
    Task<ClearanceSummary?> Clearance(Guid tenant, Guid campus, Guid employee, CancellationToken ct);
    Task<WorkflowReceipt> BindIdentity(Guid tenant, string actor, EmployeeWorkflowRequest request, string key, byte[] digest, CancellationToken ct);
    Task<WorkflowReceipt> Exit(Guid tenant, string actor, EmployeeWorkflowRequest request, string key, byte[] digest, CancellationToken ct);
    Task<WorkflowReceipt> Purchase(Guid tenant, string actor, PurchaseWorkflowRequest request, string key, byte[] digest, CancellationToken ct);
}
public sealed class CrossModuleService(ICrossModuleStore store, ITenantContext context)
{
    private async Task<TenantIdentity> Admin(CancellationToken ct)
    {
        if (!context.TryGet(out var actor) || actor is null || !await store.IsAdminAsync(actor, ct))
            throw new UnauthorizedAccessException("Active tenant administrator required.");
        return actor;
    }
    private static void Validate(Guid campus, Guid entity)
    {
        if (campus == Guid.Empty || entity == Guid.Empty) throw new ArgumentException("Campus and entity identifiers are required.");
    }
    private static string Key(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 ||
            !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ArgumentException("A valid Idempotency-Key header is required.");
        return key;
    }
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    public async Task<ClearanceSummary> Clearance(Guid campus, Guid employee, CancellationToken ct)
    {
        var actor = await Admin(ct); Validate(campus, employee);
        return await store.Clearance(actor.TenantId, campus, employee, ct) ?? throw new WorkflowScopeException();
    }
    public async Task<WorkflowReceipt> Bind(EmployeeWorkflowRequest request, string? key, CancellationToken ct)
    {
        var actor = await Admin(ct); Validate(request.CampusId, request.EmployeeId);
        if (request.UserId is null or { } user || user == Guid.Empty)
            throw new ArgumentException("Existing identity user is required.");
        var digest = Hash($"bind:{request.CampusId:D}:{request.EmployeeId:D}:{user:D}");
        return await store.BindIdentity(actor.TenantId, actor.SubjectId, request, Key(key), digest, ct);
    }
    public async Task<WorkflowReceipt> Offboard(EmployeeWorkflowRequest request, string? key, CancellationToken ct)
    {
        var actor = await Admin(ct); Validate(request.CampusId, request.EmployeeId);
        if (request.UserId is not null) throw new ArgumentException("UserId must not be supplied for offboarding.");
        var digest = Hash($"exit:{request.CampusId:D}:{request.EmployeeId:D}");
        return await store.Exit(actor.TenantId, actor.SubjectId, request, Key(key), digest, ct);
    }
    public async Task<WorkflowReceipt> RequestFinance(PurchaseWorkflowRequest request, string? key, CancellationToken ct)
    {
        var actor = await Admin(ct); Validate(request.CampusId, request.PurchaseId);
        var digest = Hash($"purchase:{request.CampusId:D}:{request.PurchaseId:D}");
        return await store.Purchase(actor.TenantId, actor.SubjectId, request, Key(key), digest, ct);
    }
}
