using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles="ADMIN")]
[Route("admin/attendance")]
public sealed class AttendanceAdminController:Controller
{
 [HttpGet("")]
 public IActionResult Index()=>View();
}
