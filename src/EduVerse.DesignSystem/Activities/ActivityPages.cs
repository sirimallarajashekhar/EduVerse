using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace EduVerse.DesignSystem.Activities;

public sealed record ActivityLink(string Key,string Title,string? TemplateKey=null,string? FixedField=null,string? FixedValue=null);
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
        if(selected is null)
        {
            // Old combined-operation URLs lead to the menu rather than reopening a mixed form.
            if(menu.Activities.Any(x=>x.TemplateKey==activity))return controller.Redirect(menu.BasePath);
            return controller.NotFound();
        }
        controller.ViewData["ActivityKey"]=selected.TemplateKey??selected.Key;
        controller.ViewData["ActivityRouteKey"]=selected.Key;
        controller.ViewData["ActivityFixedField"]=selected.FixedField;
        controller.ViewData["ActivityFixedValue"]=selected.FixedValue;
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
        if(output.Attributes.ContainsName("data-ev-action-key"))
        {
            output.Attributes.SetAttribute("data-ev-action-key",ViewContext.ViewData["ActivityRouteKey"]);
            output.Attributes.SetAttribute("data-ev-action",ViewContext.ViewData["ActivityTitle"]);
        }
        // A focused activity uses the full content width, not the old multi-column grid.
        var css=output.Attributes["class"]?.Value?.ToString();
        if(css is not null && css.Split(' ').Any(x=>x.StartsWith("col-",StringComparison.Ordinal)))
            output.Attributes.SetAttribute("class","col-12");
    }
}

/// <summary>Display a fixed operation while retaining the field ID used by the API adapter.</summary>
[HtmlTargetElement("select",Attributes="ev-fixed-operation")]
public sealed class FixedOperationTagHelper:TagHelper
{
    [ViewContext,HtmlAttributeNotBound] public ViewContext ViewContext {get;set;}=null!;
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        output.Attributes.RemoveAll("ev-fixed-operation");
        var id=output.Attributes["id"]?.Value?.ToString();
        if(id is null || id!=ViewContext.ViewData["ActivityFixedField"] as string)return;
        var input=new TagBuilder("input");
        input.Attributes["type"]="hidden";
        input.Attributes["id"]=id;
        input.Attributes["name"]=output.Attributes["name"]?.Value?.ToString()??id;
        input.Attributes["value"]=ViewContext.ViewData["ActivityFixedValue"] as string??string.Empty;
        output.TagName="span";
        output.TagMode=TagMode.StartTagAndEndTag;
        output.Attributes.Clear();
        output.Attributes.SetAttribute("class","form-control-plaintext");
        output.Content.SetHtmlContent(input);
        output.Content.Append(ViewContext.ViewData["ActivityTitle"] as string??string.Empty);
    }
}

[HtmlTargetElement(Attributes="ev-requested-activities")]
public sealed class RequestedActivityTagHelper:TagHelper
{
    [HtmlAttributeName("ev-requested-activities")] public string Activities {get;set;}=string.Empty;
    [ViewContext,HtmlAttributeNotBound] public ViewContext ViewContext {get;set;}=null!;
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        output.Attributes.RemoveAll("ev-requested-activities");
        var requested=ViewContext.ViewData["ActivityRouteKey"] as string;
        if(requested is null || !Activities.Split(',').Contains(requested,StringComparer.Ordinal))output.SuppressOutput();
    }
}

[HtmlTargetElement("button",Attributes="ev-activity-submit")]
public sealed class ActivitySubmitTagHelper:TagHelper
{
    [ViewContext,HtmlAttributeNotBound] public ViewContext ViewContext {get;set;}=null!;
    public override void Process(TagHelperContext context,TagHelperOutput output)
    {
        output.Attributes.RemoveAll("ev-activity-submit");
        output.Content.SetContent(ViewContext.ViewData["ActivityTitle"] as string??"Save");
    }
}
