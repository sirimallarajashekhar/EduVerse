using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Ams.Web.Controllers;

[Authorize]
[Route("ams")]
public sealed class ModuleController : Controller
{
    [HttpGet("")]
    [HttpGet("activities/{activity}")]
    public IActionResult Index(string? activity=null)=>this.ActivityPage("ams",activity);

    [AllowAnonymous, HttpGet("error")]
    public IActionResult Error() => View();
}
