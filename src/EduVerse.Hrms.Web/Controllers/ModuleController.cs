using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Hrms.Web.Controllers;

[Authorize]
[Route("hrms")]
public sealed class ModuleController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [AllowAnonymous, HttpGet("error")]
    public IActionResult Error() => View();
}
