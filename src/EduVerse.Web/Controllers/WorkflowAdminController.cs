using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("admin/workflows")]
public sealed class WorkflowAdminController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
