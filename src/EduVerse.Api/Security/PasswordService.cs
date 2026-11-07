using EduVerse.Application.Identity;
using Microsoft.AspNetCore.Identity;

namespace EduVerse.Api.Security;

/// <summary>ASP.NET Core Identity's versioned, salted PBKDF2 password hash format.</summary>
public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> hasher = new();
    public string Hash(string password) => hasher.HashPassword(this, password);
    public bool Verify(string hash, string password, out bool rehashNeeded)
    {
        var result = hasher.VerifyHashedPassword(this, hash, password);
        rehashNeeded = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
