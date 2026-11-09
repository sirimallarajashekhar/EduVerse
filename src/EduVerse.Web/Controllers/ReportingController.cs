using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>
/// Institution reporting is presentation-only here. Protected tenant REST/BFF endpoints
/// remain the source of metrics, subscriptions, and authorization decisions.
/// </summary>
[Authorize(Roles = "ADMIN")]
[Route("admin/reports")]
public sealed class ReportingController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
