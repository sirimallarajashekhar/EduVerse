using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Ams.Web.Controllers;

[Route("ams")]
public sealed class ModuleController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [HttpGet("error")]
    public IActionResult Error() => View();
}
