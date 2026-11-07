using System.Net.Http.Json;
using EduVerse.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduVerse.Web.Controllers;

/// <summary>Public application collection only; no student lookup or approval is exposed.</summary>
[AllowAnonymous]
[Route("apply")]
public sealed class PublicAdmissionController(IHttpClientFactory clients):Controller
{
    [HttpGet("")]
    public IActionResult Index(Guid campusId)=>View(new PublicAdmissionForm {CampusId=campusId});
    [HttpPost(""),ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(PublicAdmissionForm form,CancellationToken ct)
    {
        if(form.CampusId==Guid.Empty||string.IsNullOrWhiteSpace(form.GivenName)||form.DateOfBirth==default)
        {
            ModelState.AddModelError("","Complete required fields and use a valid campus application link.");
            return View(form);
        }
        try
        {
            using var response=await clients.CreateClient("EduVerseApi").PostAsJsonAsync(
                "api/v1/admissions/apply",
                new AdmissionSubmit(form.CampusId,form.GivenName,form.FamilyName,form.DateOfBirth,
                    form.Email,form.Phone),ct);
            if(response.IsSuccessStatusCode)
            {
                var receipt=await response.Content.ReadFromJsonAsync<AdmissionReceipt>(cancellationToken:ct);
                ViewData["Receipt"]=receipt?.ApplicationNumber.ToString();
                return View(new PublicAdmissionForm{CampusId=form.CampusId});
            }
            ModelState.AddModelError("","Application could not be accepted. Please check your details.");
        }
        catch(HttpRequestException)
        {
            ModelState.AddModelError("","The application service is temporarily unavailable.");
        }
        return View(form);
    }
}
public sealed class PublicAdmissionForm
{
    public Guid CampusId {get;set;}
    public string GivenName {get;set;}="";
    public string FamilyName {get;set;}="";
    public DateOnly DateOfBirth {get;set;}
    public string? Email {get;set;}
    public string? Phone {get;set;}
}
