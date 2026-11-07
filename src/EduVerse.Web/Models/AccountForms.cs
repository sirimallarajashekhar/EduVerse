namespace EduVerse.Web.Models;

public sealed class LoginForm
{
    public string TenantSlug { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? ReturnUrl { get; set; }
}
public sealed class ResetRequestForm
{
    public string Email { get; set; } = string.Empty;
}
public sealed class ResetConfirmForm
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
