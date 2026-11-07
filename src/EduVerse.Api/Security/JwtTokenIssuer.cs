using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EduVerse.Application.Identity;
using Microsoft.IdentityModel.Tokens;

namespace EduVerse.Api.Security;

public sealed class JwtTokenIssuer(IConfiguration config) : IAccessTokenIssuer
{
    public string Issue(Guid userId, Guid tenantId, Guid sessionId, string role, DateTimeOffset expiresUtc)
    {
        var key = config["Authentication:SigningKey"]
            ?? throw new InvalidOperationException("Authentication:SigningKey must be configured in secrets.");
        if (Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Authentication:SigningKey must contain at least 32 UTF-8 bytes.");

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new Claim("tenant_id", tenantId.ToString("D")),
            new Claim("sid", sessionId.ToString("D")),
            new Claim("role", role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var token = new JwtSecurityToken(
            issuer: config["Authentication:Issuer"],
            audience: config["Authentication:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresUtc.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
