using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles="ADMIN")]
[Route("admin/rbac")]
public sealed class RbacAdminController:Controller
{
    [HttpGet("")]public IActionResult Index()=>View();
}
