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

    /// <summary>
    /// Brotli, else gzip, for the pages themselves (the home page: about 86 KB of HTML → about 12 KB). Static files
    /// are compressed once at publish by MapStaticAssets. HTTPS included: the antiforgery token changes with each
    /// page, which takes away what a BREACH-style attack would need.
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
