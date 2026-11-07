using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Ams.Web.Controllers;

[Authorize]
[Route("ams")]
public sealed class ModuleController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();

    [AllowAnonymous, HttpGet("error")]
    public IActionResult Error() => View();
}
