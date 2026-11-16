using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduVerse.Web.Controllers;
[Authorize(Roles="ADMIN")]
[Route("admin/college")]
public sealed class CollegeAdminController:Controller
{
 [HttpGet("")]
    [HttpGet("activities/{activity}")]
 public IActionResult Index(string? activity=null)=>this.ActivityPage("college",activity);
}
