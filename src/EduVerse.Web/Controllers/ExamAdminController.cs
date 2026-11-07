using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;
[Authorize(Roles="ADMIN")]
[Route("admin/exams")]
public sealed class ExamAdminController:Controller
{
 [HttpGet("")]
 public IActionResult Index()=>View();
}
