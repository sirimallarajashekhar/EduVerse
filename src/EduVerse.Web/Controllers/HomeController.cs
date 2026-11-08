using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace EduVerse.Web.Controllers;

public sealed class HomeController : Controller
{
    [HttpGet]
    public IActionResult Index() =>
        User.Identity?.IsAuthenticated==true
            ? (User.IsInRole("PLATFORM_SUPER_ADMIN")
                ? Redirect("/platform/console")
                : Redirect("/modules"))
            : View();

    [Authorize,HttpGet("/modules")]
    public IActionResult Modules() =>
        User.IsInRole("PLATFORM_SUPER_ADMIN")
            ? Redirect("/platform/console")
            : View();

    [HttpGet]
    public IActionResult Error() => View();
}
