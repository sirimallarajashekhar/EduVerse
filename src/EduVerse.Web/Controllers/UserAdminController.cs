using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Serves a Razor view only; user records and permissions are API-owned.</summary>
[Authorize(Roles = "ADMIN")]
[Route("admin/users")]
public sealed class UserAdminController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
