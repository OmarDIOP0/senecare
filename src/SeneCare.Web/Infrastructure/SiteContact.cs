using System.Data.Common;
using Microsoft.Extensions.Caching.Memory;
using SeneCare.Application.Public;
using SeneCare.Infrastructure.Caching;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// SENECARE's contact details for the views (Admin › Paramètres): shown on every page, so kept in memory and read
/// again only when a setting is saved (<see cref="PublicCacheInvalidator"/>) or after half an hour.
/// </summary>
public sealed partial class SiteContact(GetContactHandler handler, IMemoryCache cache, ILogger<SiteContact> logger)
{
    private static readonly PublicContact None = new(null, null, null, null, null, null, null, []);
    private PublicContact? _contact;

    /// <summary>
    /// Never fails the page: when the settings cannot be read (database down), the public pages — the offline
    /// page above all — are shown without the contact details, which are hidden like empty fields.
    /// </summary>
    public async Task<PublicContact> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_contact is not null)
        {
            return _contact;
        }

        try
        {
            // A failure is not cached: the next page tries the database again.
            return _contact = (await cache.GetOrCreateAsync(PublicCache.Contact, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = PublicCache.ContactLifetime;
                return handler.HandleAsync(cancellationToken);
            }))!;
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException)
        {
            LogUnavailable(logger, exception);
            return _contact = None;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Contact details unavailable; pages shown without them.")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
