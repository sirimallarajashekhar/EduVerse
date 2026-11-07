using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Public request form. Does not provision an active tenant or user.</summary>
[AllowAnonymous]
[Route("register-institution")]
public sealed class RegistrationController(IHttpClientFactory clients):Controller
{
    [HttpGet("")]
    public IActionResult Index()=>View(new RegistrationForm());

    [HttpPost(""),ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(RegistrationForm form,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(form.Slug) || string.IsNullOrWhiteSpace(form.Name) ||
           string.IsNullOrWhiteSpace(form.Kind) || string.IsNullOrWhiteSpace(form.Email))
        {
            ModelState.AddModelError("","Complete all fields.");
            return View(form);
        }
        try
        {
            using var response=await clients.CreateClient("EduVerseApi")
                .PostAsJsonAsync("api/v1/institution-registration/requests",
                    new RegistrationRequest(form.Slug,form.Name,form.Kind,form.Email),ct);
            if(response.IsSuccessStatusCode)
            {
                ViewData["Submitted"]=true;
                return View(new RegistrationForm());
            }
            ModelState.AddModelError("","Registration was not accepted. Check the fields and try again.");
        }
        catch(HttpRequestException)
        {
            ModelState.AddModelError("","Registration service is unavailable.");
        }
        return View(form);
    }
}

public sealed class RegistrationForm
{
    public string Slug {get;set;}=string.Empty;
    public string Name {get;set;}=string.Empty;
    public string Kind {get;set;}="School";
    public string Email {get;set;}=string.Empty;
}
