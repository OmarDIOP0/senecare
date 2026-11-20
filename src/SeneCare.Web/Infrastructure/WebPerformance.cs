using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace SeneCare.Web.Infrastructure;

/// <summary>Bytes on the wire: compressed pages, static files the browser keeps (3G, expensive data).</summary>
public static class WebPerformance
{
    /// <summary>Folders whose files never change under the same name once versioned (« ?v=… »).</summary>
    private static readonly string[] StaticFolders = ["/css/", "/js/", "/lib/", "/fonts/", "/img/", "/images/"];

    /// <summary>Folders of stable files linked without a version (fonts from the CSS, photos of the public site).</summary>
    private static readonly string[] StableFolders = ["/fonts/", "/img/", "/images/"];

    /// <summary>Signed-in screens that show secrets or other people's data: never compressed (BREACH, see below).</summary>
    private static readonly string[] SensitivePaths = ["/admin", "/compte", "/entreprise", "/invitation"];

    /// <summary>
    /// Brotli, else gzip, for the pages themselves (the home page: about 86 KB of HTML → about 19 KB). Static files
    /// are compressed once at publish by MapStaticAssets. Over HTTPS, compressing a page that holds a secret and
    /// text chosen by an attacker can leak that secret through response sizes (BREACH): see
    /// <see cref="UseSeneCareCompression"/> for what is left out.
    /// </summary>
    public static IServiceCollection AddSeneCareCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["image/svg+xml", "application/manifest+json"]);
        });
        // « Fastest »: pages are made on the fly; the best ratio would cost more CPU than it saves on the network.
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        return services;
    }

    /// <summary>
    /// Compression for anonymous visitors (the public site, most of the traffic on 3G) and for the customer's own
    /// pages; not for signed-in screens with temporary passwords, invitation links, other people's files or exports.
    /// The sign-in cookie is SameSite=Lax as well, so a foreign page cannot replay signed-in requests. Place after
    /// UseAuthentication (the user must be known).
    /// </summary>
    public static IApplicationBuilder UseSeneCareCompression(this IApplicationBuilder app) =>
        app.UseWhen(
            context => context.User.Identity?.IsAuthenticated != true
                || !SensitivePaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)),
            branch => branch.UseResponseCompression());

    /// <summary>
    /// Versioned static files (« app.css?v=… »): kept a year, never asked again (a new version has a new URL).
    /// Fonts and public images without a version: a week. Anything else keeps its own policy: a script linked
    /// without a version must not stay stale after an update.
    /// </summary>
    public static IApplicationBuilder UseStaticFileCaching(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (HttpMethods.IsGet(context.Request.Method) && StaticFolders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
            {
                var versioned = context.Request.Query.ContainsKey("v");
                var stable = StableFolders.Any(f => path.StartsWith(f, StringComparison.OrdinalIgnoreCase));
                if (versioned || stable)
                {
                    context.Response.OnStarting(() =>
                    {
                        if (context.Response.StatusCode == StatusCodes.Status200OK)
                        {
                            context.Response.Headers.CacheControl = versioned ? "public, max-age=31536000, immutable" : "public, max-age=604800";
                        }

                        return Task.CompletedTask;
                    });
                }
            }

            return next(context);
        });
}
