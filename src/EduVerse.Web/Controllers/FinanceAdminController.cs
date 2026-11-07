using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles="ADMIN")]
[Route("admin/finance")]
public sealed class FinanceAdminController:Controller
{
 [HttpGet("")]
 public IActionResult Index()=>View();
}
