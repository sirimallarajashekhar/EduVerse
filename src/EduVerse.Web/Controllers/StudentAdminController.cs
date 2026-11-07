using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Thin Razor page; all student data comes from protected REST APIs.</summary>
[Authorize(Roles="ADMIN")]
[Route("admin/students")]
public sealed class StudentAdminController:Controller
{
    [HttpGet("")]
    public IActionResult Index()=>View();
}
