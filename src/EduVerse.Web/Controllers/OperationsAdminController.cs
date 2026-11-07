using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduVerse.Web.Controllers;
[Authorize(Roles="ADMIN")]
[Route("admin/operations")]
public sealed class OperationsAdminController:Controller
{
 [HttpGet("")]
 public IActionResult Index()=>View();
}
