using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SeneCare.Web.Controllers;

/// <summary>Service worker (generated with versioned asset URLs) and the offline page.</summary>
[AllowAnonymous]
public sealed class PwaController(IFileVersionProvider versions) : Controller
{
    /// <summary>
    /// Forms that can be submitted offline: the worker queues them when the network fails.
    /// An entry ending with « / » covers every form below it (the technician's field actions).
    /// </summary>
    public static readonly string[] OfflinePosts = ["/signaler", "/design/test-hors-ligne", "/technicien/"];

    private static readonly string Template = LoadTemplate();

    private static readonly string[] ShellAssets =
    [
        "/css/app.css",
        "/js/outbox.js",
        "/js/app.js",
        "/lib/htmx.min.js",
        "/img/icons.svg",
        "/img/icons/favicon.svg",
        "/fonts/bricolage-grotesque-latin-400-normal.woff2",
        "/fonts/bricolage-grotesque-latin-600-normal.woff2",
        "/fonts/bricolage-grotesque-latin-700-normal.woff2",
        "/fonts/unbounded-latin-700-normal.woff2",
        "/fonts/space-mono-latin-700-normal.woff2",
        "/img/motifs/bande.svg",
    ];

    /// <summary>
    /// The script embeds the versioned URLs, so any asset change produces a new worker
    /// and the « Nouvelle version disponible » banner.
    /// </summary>
    [HttpGet("/sw.js")]
    public ContentResult ServiceWorker()
    {
        var precache = ShellAssets
            .Select(path => versions.AddFileVersionToPath(Request.PathBase, path))
            .Prepend("/hors-ligne")
            .Append("/manifest.webmanifest")
            .ToArray();
        var precacheJson = JsonSerializer.Serialize(precache);
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Template + precacheJson)))[..12].ToLowerInvariant();

        var script = Template
            .Replace("__VERSION__", version, StringComparison.Ordinal)
            .Replace("__PRECACHE__", precacheJson, StringComparison.Ordinal)
            .Replace("__OFFLINE_POSTS__", JsonSerializer.Serialize(OfflinePosts), StringComparison.Ordinal);

        Response.Headers.CacheControl = "no-cache";
        Response.Headers["Service-Worker-Allowed"] = "/";
        return Content(script, "text/javascript; charset=utf-8");
    }

    [HttpGet("/hors-ligne")]
    public IActionResult Offline([FromQuery(Name = "en-attente")] bool pending) => View(pending);

    private static string LoadTemplate()
    {
        using var stream = typeof(PwaController).Assembly.GetManifestResourceStream("SeneCare.Web.Pwa.sw.js")
            ?? throw new InvalidOperationException("Service worker template missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
