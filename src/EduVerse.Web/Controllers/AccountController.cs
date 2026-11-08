using System.Net.Http.Json;
using System.Security.Claims;
using EduVerse.AuthWeb;
using EduVerse.Contracts;
using EduVerse.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Presentation-only authentication adapter. Identity API makes all credential and membership decisions.</summary>
[Route("account")]
public sealed class AccountController(IHttpClientFactory clients) : Controller
{
    [AllowAnonymous, HttpGet("login")]
    public IActionResult Login(string? returnUrl = null) => View(new LoginForm { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost("login"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.TenantSlug) ||
            string.IsNullOrWhiteSpace(form.Email) || string.IsNullOrWhiteSpace(form.Password))
        {
            ModelState.AddModelError("", "Institution, email and password are required.");
            return View(form);
        }

        try
        {
            using var response = await clients.CreateClient("EduVerseIdentityApi")
                .PostAsJsonAsync("api/v1/identity/login",
                    new LoginRequest(form.TenantSlug, form.Email, form.Password), ct);
            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError("", "Invalid credentials or institution.");
                return View(form);
            }
            var token = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            if (token is null) return StatusCode(502);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, token.SubjectId),
                new Claim(ClaimTypes.Name, token.DisplayName),
                new Claim(ClaimTypes.Email, token.Email),
                new Claim("tenant_id", token.TenantId.ToString("D")),
                new Claim(ClaimTypes.Role, token.RoleCode)
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, EduVerseCookieAuth.Scheme));
            var properties = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = token.ExpiresUtc,
                AllowRefresh = false
            };
            properties.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = token.AccessToken } });
            await HttpContext.SignInAsync(EduVerseCookieAuth.Scheme, principal, properties);
            return Url.IsLocalUrl(form.ReturnUrl) && !form.ReturnUrl.StartsWith("/platform",StringComparison.OrdinalIgnoreCase)
                ? LocalRedirect(form.ReturnUrl) : Redirect("/modules");
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "Sign-in service unavailable.");
            return View(form);
        }
    }

    [Authorize, HttpPost("logout"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var token = await HttpContext.GetTokenAsync(EduVerseCookieAuth.Scheme, "access_token");
        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/identity/logout");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using var response = await clients.CreateClient("EduVerseIdentityApi").SendAsync(request, ct);
            }
            catch (HttpRequestException) { /* Cookie is still cleared; API session expires in <= 20 min. */ }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { }
        }
        await HttpContext.SignOutAsync(EduVerseCookieAuth.Scheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous, HttpGet("password-reset")]
    public IActionResult PasswordReset() => View(new ResetRequestForm());

    [AllowAnonymous, HttpPost("password-reset"), ValidateAntiForgeryToken]
    public async Task<IActionResult> PasswordReset(ResetRequestForm form, CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("EduVerseIdentityApi")
                .PostAsJsonAsync("api/v1/identity/password-reset/request", new PasswordResetRequest(form.Email), ct);
        }
        catch (HttpRequestException) { }
        TempData["Info"] = "If this account exists, a reset email will be sent.";
        return RedirectToAction(nameof(PasswordReset));
    }

    [AllowAnonymous, HttpGet("reset")]
    public IActionResult Reset(string token) => View(new ResetConfirmForm { Token = token });

    [AllowAnonymous, HttpPost("reset"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Reset(ResetConfirmForm form, CancellationToken ct)
    {
        try
        {
            using var response = await clients.CreateClient("EduVerseIdentityApi")
                .PostAsJsonAsync("api/v1/identity/password-reset/confirm",
                    new PasswordResetConfirm(form.Token, form.Password), ct);
            if (response.IsSuccessStatusCode)
            {
                TempData["Info"] = "Password updated. Sign in with your new password.";
                return RedirectToAction(nameof(Login));
            }
        }
        catch (HttpRequestException) { }
        ModelState.AddModelError("", "Reset link expired or password does not meet requirements.");
        return View(form);
    }

    [AllowAnonymous, HttpGet("denied")]
    public IActionResult Denied() => View();
}
