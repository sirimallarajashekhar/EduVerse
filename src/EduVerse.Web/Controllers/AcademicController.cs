using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin Razor view: no ORM, database or academic business logic in this layer.</summary>
[Authorize(Roles="ADMIN")]
[Route("admin/academics")]
public sealed class AcademicController:Controller
{
    [HttpGet("")]
    [HttpGet("activities/{activity}")]
    public IActionResult Index(string? activity=null)=>this.ActivityPage("academics",activity);
}
