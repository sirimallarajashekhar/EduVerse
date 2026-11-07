using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin Razor view: no ORM, database or academic business logic in this layer.</summary>
[Authorize(Roles="ADMIN")]
[Route("admin/academics")]
public sealed class AcademicController:Controller
{
    [HttpGet("")]
    public IActionResult Index()=>View();
}
