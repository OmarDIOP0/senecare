using Microsoft.Extensions.Caching.Memory;
using SeneCare.Application.Public;
using SeneCare.Infrastructure.Caching;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// Offers, figures, advice and reviews of the public pages (home, prices, trades, sign-up): seven queries, so
/// built once and shared by every visitor for a few minutes. An admin edit (prices, settings, advice) or a new /
/// hidden review empties it at once (<see cref="PublicCacheInvalidator"/>).
/// </summary>
public sealed class CachedPublicHome(GetPublicHomeHandler handler, IMemoryCache cache, PublicCacheSignals signals)
{
    public async Task<PublicHome> HandleAsync(CancellationToken cancellationToken = default) =>
        (await cache.GetOrCreateAsync(PublicCache.Home, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PublicCache.HomeLifetime;
            // Taken before the queries: an erasure or a hidden review during them makes this entry stale at once.
            entry.AddExpirationToken(signals.TokenFor(PublicCache.Home));
            return handler.HandleAsync(cancellationToken);
        }))!;
}
