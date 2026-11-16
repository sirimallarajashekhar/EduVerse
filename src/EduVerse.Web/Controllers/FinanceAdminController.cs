using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

[Authorize(Roles="ADMIN")]
[Route("admin/finance")]
public sealed class FinanceAdminController:Controller
{
 [HttpGet("")]
    [HttpGet("activities/{activity}")]
 public IActionResult Index(string? activity=null)=>this.ActivityPage("finance",activity);
}
