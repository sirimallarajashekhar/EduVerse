using System.Net;
using System.Net.Mail;
using EduVerse.Application.Identity;

namespace EduVerse.Api.Security;

/// <summary>Sends reset token directly through configured SMTP. Never places reset secrets in logs or API responses.</summary>
public sealed class SmtpPasswordResetMessenger(IConfiguration config) : IPasswordResetMessenger
{
    public async Task SendAsync(string email, string token, CancellationToken ct)
    {
        var host = config["PasswordReset:Smtp:Host"]
            ?? throw new InvalidOperationException("PasswordReset:Smtp:Host is not configured.");
        var from = config["PasswordReset:Smtp:From"]
            ?? throw new InvalidOperationException("PasswordReset:Smtp:From is not configured.");
        var baseUrl = config["PasswordReset:Url"]
            ?? throw new InvalidOperationException("PasswordReset:Url is not configured.");
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("PasswordReset:Url must be HTTPS.");

        var url = baseUrl.TrimEnd('/') + "?token=" + Uri.EscapeDataString(token);
        using var message = new MailMessage(from, email)
        {
            Subject = "EduVerse password reset",
            Body = "To set your EduVerse password, open this one-time link (valid for 15 minutes):\n" + url
        };
        using var smtp = new SmtpClient(host, int.TryParse(config["PasswordReset:Smtp:Port"], out var port) ? port : 587)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(config["PasswordReset:Smtp:Username"], config["PasswordReset:Smtp:Password"])
        };
        await smtp.SendMailAsync(message, ct);
    }
}
