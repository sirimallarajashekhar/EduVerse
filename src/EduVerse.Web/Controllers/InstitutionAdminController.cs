using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Renders only the dashboard shell; all institution data is loaded from REST API.</summary>
[Authorize(Roles="ADMIN")]
[Route("admin/institution")]
public sealed class InstitutionAdminController:Controller
{
    [HttpGet("")]
    public IActionResult Index()=>View();
}
