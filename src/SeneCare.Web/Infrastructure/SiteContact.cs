using SeneCare.Application.Public;

namespace SeneCare.Web.Infrastructure;

/// <summary>SENECARE's contact details for the views (Admin › Paramètres), read once per request.</summary>
public sealed class SiteContact(GetContactHandler handler)
{
    private PublicContact? _contact;

    public async Task<PublicContact> GetAsync(CancellationToken cancellationToken = default) =>
        _contact ??= await handler.HandleAsync(cancellationToken);
}
