using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Presentation only. REST API checks role, school, campus and record ownership.</summary>
[Authorize]
[Route("portal")]
public sealed class RolePortalController:Controller
{
    [HttpGet("teacher")] public IActionResult Teacher()=>View();
    [HttpGet("parent")] public IActionResult Parent()=>View();
    [HttpGet("student")] public IActionResult Student()=>View();
}
