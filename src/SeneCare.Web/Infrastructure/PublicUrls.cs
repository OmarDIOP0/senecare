using Microsoft.Extensions.Options;

namespace SeneCare.Web.Infrastructure;

/// <summary>SEO settings (<c>Seo:*</c>): the address search engines should keep for every page.</summary>
public sealed class SeoOptions
{
    /// <summary>
    /// « https://senecare.net »: canonical links, sitemap and share previews always point there, whatever address the
    /// visitor typed (www, test site). Empty: the address of the request.
    /// </summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Absolute addresses of public pages (canonical link, sitemap, Open Graph).</summary>
public sealed class PublicUrls(IOptions<SeoOptions> options)
{
    private readonly string? _base = Uri.TryCreate(options.Value.BaseUrl, UriKind.Absolute, out var root) && root.Scheme == Uri.UriSchemeHttps
        ? root.GetLeftPart(UriPartial.Authority)
        : null;

    public string Origin(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _base ?? $"{request.Scheme}://{request.Host}";
    }

    /// <summary><paramref name="path"/> (« /tarifs ») on the public address; no query string (one page, one address).</summary>
    public string Absolute(HttpRequest request, string path) => Origin(request) + (path.StartsWith('/') ? path : "/" + path);
}
