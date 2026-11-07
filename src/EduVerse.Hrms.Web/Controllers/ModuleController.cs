using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Hrms.Web.Controllers;

[Route("hrms")]
public sealed class ModuleController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("error")]
    public IActionResult Error() => View();
}
