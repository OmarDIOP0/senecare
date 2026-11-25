using System.Text.Json;
using Microsoft.AspNetCore.Html;
using SeneCare.Application.Public;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// schema.org data (JSON-LD) read by search engines: SENECARE as a local business (contact details from Admin ›
/// Paramètres, nothing written in the code) and the FAQ. The default encoder escapes « &lt; », so the JSON cannot
/// close its &lt;script&gt; element.
/// </summary>
public static class StructuredData
{
    public static IHtmlContent Business(PublicContact contact, string origin, string description)
    {
        ArgumentNullException.ThrowIfNull(contact);
        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "HomeAndConstructionBusiness",
            ["@id"] = origin + "/#entreprise",
            ["name"] = "SeneCare",
            ["slogan"] = "Du plan à la réalité.",
            ["description"] = description,
            ["url"] = origin + "/",
            ["logo"] = origin + "/img/icons/icon-512.png",
            ["image"] = origin + "/images/partage.jpg",
            ["telephone"] = contact.PhoneDisplay,
            ["email"] = contact.Email,
            ["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = contact.Address,
                ["addressCountry"] = "SN",
            },
            ["areaServed"] = string.IsNullOrWhiteSpace(contact.Areas) ? "Sénégal" : contact.Areas,
            ["sameAs"] = contact.Social.Count == 0 ? null : contact.Social.Select(s => s.Url).ToArray(),
        };
        return Script(data);
    }

    public static IHtmlContent Faq(IEnumerable<(string Question, string Answer)> items) =>
        Script(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "FAQPage",
            ["mainEntity"] = items.Select(i => new Dictionary<string, object?>
            {
                ["@type"] = "Question",
                ["name"] = i.Question,
                ["acceptedAnswer"] = new Dictionary<string, object?> { ["@type"] = "Answer", ["text"] = i.Answer },
            }).ToArray(),
        });

    private static HtmlString Script(Dictionary<string, object?> data)
    {
        // Empty contact fields (not set in Admin › Paramètres) are left out rather than sent empty.
        var json = JsonSerializer.Serialize(Clean(data));
        return new HtmlString($"<script type=\"application/ld+json\">{json}</script>");
    }

    private static Dictionary<string, object?> Clean(Dictionary<string, object?> data) =>
        data.Where(p => p.Value is not null && p.Value is not string { Length: 0 })
            .ToDictionary(p => p.Key, p => p.Value is Dictionary<string, object?> inner ? Clean(inner) : p.Value);
}
