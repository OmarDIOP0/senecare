using System.Data.Common;
using SeneCare.Application.Public;

namespace SeneCare.Web.Infrastructure;

/// <summary>SENECARE's contact details for the views (Admin › Paramètres), read once per request.</summary>
public sealed partial class SiteContact(GetContactHandler handler, ILogger<SiteContact> logger)
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
            return _contact = await handler.HandleAsync(cancellationToken);
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
