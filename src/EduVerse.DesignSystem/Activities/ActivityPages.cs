using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EduVerse.DesignSystem.Activities;

public sealed record ActivityLink(string Key,string Title);
public sealed record ActivityMenu(string Title,string BasePath,IReadOnlyList<ActivityLink> Activities);

public static class ActivityPages
{
    public static IActionResult ActivityPage(this Controller controller,string module,string? activity,string view="Index")
    {
        if(!ActivityCatalog.Menus.TryGetValue(module,out var menu))return controller.NotFound();
        controller.Response.Headers["Cache-Control"]="no-store";
        if(string.IsNullOrEmpty(activity))
        {
            controller.ViewData["Title"]=menu.Title;
            return controller.View("_ActivityHub",menu);
        }
        var selected=menu.Activities.FirstOrDefault(x=>x.Key==activity);
        if(selected is null)return controller.NotFound();
        controller.ViewData["ActivityKey"]=selected.Key;
        controller.ViewData["ActivityTitle"]=selected.Title;
        controller.ViewData["ActivityBasePath"]=menu.BasePath;
        return controller.View(view);
    }
}

/// <summary>Server-side projection: unrequested activities never enter the HTML response.</summary>
[HtmlTargetElement(Attributes="ev-activities")]
public sealed class ActivityOnlyTagHelper:TagHelper
{
    [HtmlAttributeName("ev-activities")] public string Activities {get;set;}=string.Empty;
    [ViewContext,HtmlAttributeNotBound] public ViewContext ViewContext {get;set;}=null!;
    public override int Order=>int.MaxValue;
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        var selected=ViewContext.ViewData["ActivityKey"] as string;
        output.Attributes.RemoveAll("ev-activities");
        if(selected is null || !Activities.Split(',').Contains(selected,StringComparer.Ordinal))
        {
            output.SuppressOutput();
            return;
        }
        // A focused activity uses the full content width, not the old multi-column grid.
        var css=output.Attributes["class"]?.Value?.ToString();
        if(css is not null && css.Split(' ').Any(x=>x.StartsWith("col-",StringComparison.Ordinal)))
            output.Attributes.SetAttribute("class","col-12");
    }
}
