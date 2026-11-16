using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Hrms.Web.Controllers;

[Authorize]
[Route("hrms")]
public sealed class ModuleController : Controller
{
    [HttpGet("")]
    [HttpGet("activities/{activity}")]
    public IActionResult Index(string? activity=null)=>this.ActivityPage("hrms",activity);

    [AllowAnonymous, HttpGet("error")]
    public IActionResult Error() => View();
}
