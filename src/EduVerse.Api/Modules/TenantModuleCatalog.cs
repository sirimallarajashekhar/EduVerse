using EduVerse.Contracts;

namespace EduVerse.Api.Modules;

/// <summary>Explicit route allowlist for real MVC screens; API owns module applicability and subscription policy.</summary>
public static class TenantModuleCatalog
{
    private sealed record Definition(string Id,string Name,string Route,string Feature,string Description,bool CollegeOnly=false);
    private static readonly Definition[] Catalog =
    [
        new("institution","Institution Management","/admin/institution","core","Campuses, profile and settings"),
        new("users","User Management","/admin/users","core","Institution users and access"),
        new("academics","Academics","/admin/academics","academics","Years, classes and subjects"),
        new("students","Admissions & Students","/admin/students","core","Admissions and student records"),
        new("attendance","Attendance & Timetables","/admin/attendance","academics","Daily attendance and schedules"),
        new("finance","Fees & Finance","/admin/finance","finance","Fees, collections and receipts"),
        new("exams","Exams & Results","/admin/exams","academics","Assessments and results"),
        new("operations","Library, Transport & Hostel","/admin/operations","operations","Institution operations"),
        new("learning","Learning & Communication","/admin/learning","learning","Course materials and messaging"),
        new("college","Advanced College","/admin/college","college","Semesters, electives and CGPA",true),
        new("reporting","Reporting","/admin/reporting","reports","Institution reporting"),
        new("workflows","Cross-module Workflows","/admin/workflows","workflows","Cross-module processes")
    ];
    public static IReadOnlyList<TenantModule> ForAdmin(string kind,IReadOnlyCollection<string> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if(kind is not ("School" or "College" or "Mixed"))
            throw new ArgumentException("Invalid institution type.",nameof(kind));
        return Catalog.Where(x=>!x.CollegeOnly || kind is "College" or "Mixed")
            .Select(x=>
            {
                var enabled=features.Contains(x.Feature);
                return new TenantModule(x.Id,x.Name,x.Route,x.Description,x.Feature,enabled,
                    enabled?null:"Not included in the active subscription");
            }).ToArray();
    }
}
