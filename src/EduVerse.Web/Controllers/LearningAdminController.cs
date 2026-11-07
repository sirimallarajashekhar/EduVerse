using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduVerse.Web.Controllers;
[Authorize(Roles="ADMIN")]
[Route("admin/learning")]
public sealed class LearningAdminController:Controller
{
 [HttpGet("")]
 public IActionResult Index()=>View();
}
