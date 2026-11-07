using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("admin/reporting")]
public sealed class ReportingAdminController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
