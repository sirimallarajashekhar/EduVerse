using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduVerse.Web.Controllers;
[Authorize(Roles="ADMIN")]
[Route("admin/college")]
public sealed class CollegeAdminController:Controller
{
 [HttpGet("")]
 public IActionResult Index()=>View();
}
