using EduVerse.DesignSystem.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Presentation only. REST API checks role, school, campus and record ownership.</summary>
[Authorize]
[Route("portal")]
public sealed class RolePortalController:Controller
{
    [HttpGet("teacher")] [HttpGet("teacher/activities/{activity}")] public IActionResult Teacher(string? activity=null)=>this.ActivityPage("teacher",activity,"Teacher");
    [HttpGet("parent")] [HttpGet("parent/activities/{activity}")] public IActionResult Parent(string? activity=null)=>this.ActivityPage("parent",activity,"Parent");
    [HttpGet("student")] [HttpGet("student/activities/{activity}")] public IActionResult Student(string? activity=null)=>this.ActivityPage("student",activity,"Student");
    [Authorize(Roles="ADMIN"),HttpGet("learning-review")] [HttpGet("learning-review/activities/{activity}")] public IActionResult LearningReview(string? activity=null)=>this.ActivityPage("learningreview",activity,"LearningReview");
}
