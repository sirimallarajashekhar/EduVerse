using EduVerse.Domain;

namespace EduVerse.Application.Operations;

/// <summary>Implementations must atomically persist command result + key within the same DB transaction.</summary>
public interface IIdempotentCampusWriter
{
    Task<Campus> CreateAsync(TenantIdentity identity, string code, string name,
        string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException() : base("Idempotency key was already used with a different request.") { }
}
