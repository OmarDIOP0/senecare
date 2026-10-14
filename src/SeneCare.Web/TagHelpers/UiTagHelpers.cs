using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;
using SeneCare.Web.Infrastructure;
using SeneCare.Web.Localization;

namespace SeneCare.Web.TagHelpers;

/// <summary>
/// Lucide icon from the cached sprite: <c>&lt;sc-icon name="zap" class="size-5" /&gt;</c>.
/// Decorative by default; give <c>label</c> when the icon carries meaning on its own.
/// </summary>
[HtmlTargetElement("sc-icon", TagStructure = TagStructure.WithoutEndTag)]
public sealed class IconTagHelper(IFileVersionProvider versions) : TagHelper
{
    public string Name { get; set; } = string.Empty;

    public string? Label { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var sprite = versions.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, "/img/icons.svg");
        output.TagName = "svg";
        output.TagMode = TagMode.StartTagAndEndTag;
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        // An SVG without explicit size renders at 300×150: default to 18 px unless a size-* class is given.
        var hasSize = existingClass?.Split(' ').Any(c => c.StartsWith("size-", StringComparison.Ordinal) || c.Contains(":size-", StringComparison.Ordinal)) ?? false;
        output.Attributes.SetAttribute("class", ("inline-block shrink-0 align-[-0.15em] " + (hasSize ? string.Empty : "size-[18px] ") + existingClass).Trim());
        output.Attributes.SetAttribute("fill", "none");
        output.Attributes.SetAttribute("stroke", "currentColor");
        // Lucide at 1.75: the lighter line of current product UIs.
        output.Attributes.SetAttribute("stroke-width", "1.75");
        output.Attributes.SetAttribute("stroke-linecap", "round");
        output.Attributes.SetAttribute("stroke-linejoin", "round");
        if (string.IsNullOrEmpty(Label))
        {
            output.Attributes.SetAttribute("aria-hidden", "true");
        }
        else
        {
            output.Attributes.SetAttribute("role", "img");
            output.Attributes.SetAttribute("aria-label", Label);
        }

        output.Content.SetHtmlContent($"<use href=\"{sprite}#{Name}\"></use>");
    }
}

/// <summary>Amount in FCFA: <c>&lt;sc-amount value="8500" /&gt;</c> → « 8 500 FCFA ».</summary>
[HtmlTargetElement("sc-amount", TagStructure = TagStructure.WithoutEndTag)]
public sealed class AmountTagHelper : TagHelper
{
    public long Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        var existingClass = output.Attributes["class"]?.Value?.ToString();
        output.Attributes.SetAttribute("class", ("tabular-nums whitespace-nowrap " + existingClass).Trim());
        output.Content.SetContent(Formatting.Fcfa(Value));
    }
}

/// <summary>Request status badge: <c>&lt;sc-status value="RequestStatus.OnTheWay" /&gt;</c>.</summary>
[HtmlTargetElement("sc-status", TagStructure = TagStructure.WithoutEndTag)]
public sealed class StatusBadgeTagHelper(IStringLocalizer<SharedResource> localizer) : TagHelper
{
    public RequestStatus Value { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "sc-badge " + Formatting.BadgeClass(Value));
        output.Content.SetContent(localizer["Status_" + Value]);
    }
}

/// <summary>Urgency badge with icon (colour is never the only signal).</summary>
[HtmlTargetElement("sc-urgency", TagStructure = TagStructure.WithoutEndTag)]
public sealed class UrgencyBadgeTagHelper(IStringLocalizer<SharedResource> localizer, IFileVersionProvider versions) : TagHelper
{
    public Urgency Value { get; set; }

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var sprite = versions.AddFileVersionToPath(ViewContext.HttpContext.Request.PathBase, "/img/icons.svg");
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "sc-badge " + Formatting.BadgeClass(Value));
        output.Content.SetHtmlContent(
            $"<svg class=\"size-3\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\"><use href=\"{sprite}#{Formatting.Icon(Value)}\"></use></svg>");
        output.Content.Append(localizer["Urgency_" + Value]);
    }
}

/// <summary>
/// Bottom sheet on a native &lt;dialog&gt; (focus trap, Esc, backdrop for free):
/// <c>&lt;sc-sheet id="choix" title="…"&gt;…&lt;/sc-sheet&gt;</c>, opened by any element with <c>data-sheet-open="choix"</c>.
/// </summary>
[HtmlTargetElement("sc-sheet")]
public sealed class SheetTagHelper(IStringLocalizer<SharedResource> localizer) : TagHelper
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var body = (await output.GetChildContentAsync()).GetContent();
        var titleId = Id + "-title";
        output.TagName = "dialog";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("id", Id);
        output.Attributes.SetAttribute("class", "sc-sheet");
        output.Attributes.SetAttribute("aria-labelledby", titleId);
        var close = System.Net.WebUtility.HtmlEncode(localizer["Common_Close"]);
        output.Content.SetHtmlContent(
            "<div class=\"mx-auto mt-2 h-1.5 w-12 rounded-full bg-ligne sm:hidden\" aria-hidden=\"true\"></div>"
            + "<div class=\"flex items-center justify-between gap-2 px-4 pt-3\">"
            + $"<h2 id=\"{titleId}\" class=\"sc-sous-titre\">{System.Net.WebUtility.HtmlEncode(Title)}</h2>"
            + $"<button type=\"button\" class=\"sc-btn-icone\" data-sheet-close aria-label=\"{close}\">"
            + "<svg class=\"size-6\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" aria-hidden=\"true\"><path d=\"M18 6 6 18M6 6l12 12\"/></svg></button></div>"
            + $"<div class=\"overflow-y-auto px-4 pt-2 pb-6\">{body}</div>");
    }
}
