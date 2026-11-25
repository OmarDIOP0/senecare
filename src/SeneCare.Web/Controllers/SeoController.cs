using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SeneCare.Web.Infrastructure;

namespace SeneCare.Web.Controllers;

/// <summary>
/// robots.txt and sitemap.xml for search engines: the public pages (home, trades, offers, FAQ, legal) are listed; the
/// signed-in spaces, the back office and the API are not to be crawled (their pages also say « noindex »).
/// </summary>
[AllowAnonymous]
public sealed class SeoController(PublicUrls urls, SiteContent site) : Controller
{
    /// <summary>Public pages, most important first (trade pages are added from the site content).</summary>
    private static readonly string[] Pages =
        ["/", "/metiers", "/particuliers", "/entreprises", "/quartiers", "/classement-quartiers", "/tarifs", "/faq", "/mentions-legales", "/cgu", "/confidentialite"];

    /// <summary>Prefixes (robots.txt matches the start of the path: « /entreprise/ » keeps « /entreprises » open).</summary>
    private static readonly string[] Private =
        ["/admin", "/api", "/hubs", "/compte", "/entreprise/", "/technicien", "/abonnement", "/interventions", "/logements",
         "/signaler", "/connexion", "/inscription", "/invitation", "/notifications", "/zones/attente", "/paiements", "/espace", "/design", "/fichiers",
         "/deconnexion", "/quartiers/recherche", "/visite", "/hors-ligne", "/acces-refuse"];

    [HttpGet("/robots.txt")]
    [ResponseCache(Duration = 86400)]
    public IActionResult Robots()
    {
        var text = new StringBuilder("User-agent: *\n");
        foreach (var path in Private)
        {
            text.Append("Disallow: ").Append(path).Append('\n');
        }

        text.Append("\nSitemap: ").Append(urls.Absolute(Request, "/sitemap.xml")).Append('\n');
        return Content(text.ToString(), "text/plain; charset=utf-8");
    }

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 86400)]
    public IActionResult Sitemap()
    {
        var paths = Pages.Concat(site.Trades.Select(t => "/metiers/" + t.Slug)).Distinct(StringComparer.Ordinal);
        var output = new StringBuilder();
        using (var xml = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var path in paths)
            {
                xml.WriteStartElement("url");
                xml.WriteElementString("loc", urls.Absolute(Request, path));
                xml.WriteElementString("priority", path == "/" ? "1.0" : path.StartsWith("/metiers/", StringComparison.Ordinal) ? "0.8" : "0.6");
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        }

        // XmlWriter over a StringBuilder declares UTF-16: the file is sent as UTF-8.
        return Content(output.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"", StringComparison.Ordinal), "application/xml; charset=utf-8");
    }
}
