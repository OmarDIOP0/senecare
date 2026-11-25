using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Properties;

public sealed record AddPropertyCommand(
    Guid UserId,
    PropertyKind Kind,
    string? Name,
    Guid? NeighborhoodId,
    double? Latitude,
    double? Longitude,
    string? Landmark,
    string? Details,
    IReadOnlyList<UploadedFile> Photos);

/// <summary>
/// « Ajouter mon logement » / « Ajouter un site ». For a household, each home gets its own « Maisons » (or « Villas »)
/// subscription, created pending until the registration fee and first month are paid (Phase 6).
/// </summary>
public sealed class AddPropertyHandler(IApplicationDbContext db, AccessControl access, MediaUploader uploader, Locations.Coverage coverage, TimeProvider clock)
{
    public const string HouseholdPlanCode = "HOME";

    /// <summary>Offer of the homes declared as a villa (user decision of 25/11/2026), when it is active.</summary>
    public const string VillaPlanCode = "VILLA";

    public async Task<Result<Guid>> HandleAsync(AddPropertyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var account = (await access.AccountsOfAsync(command.UserId, cancellationToken))
            .FirstOrDefault(a => a.FindMember(command.UserId)!.HasFullAccess);
        if (account is null)
        {
            return Result<Guid>.Failure("property.no_account");
        }

        if (command.NeighborhoodId is not { } neighborhoodId || !await db.Neighborhoods.AnyAsync(n => n.Id == neighborhoodId, cancellationToken))
        {
            return Result<Guid>.Failure("address.neighborhood");
        }

        // A new home only in a zone SENECARE covers (the form offers « Prévenez-moi » elsewhere).
        if (!await coverage.IsServedAsync(neighborhoodId, cancellationToken))
        {
            return Result<Guid>.Failure("place.not_served");
        }

        if (command.Photos.Count > Property.MaxPhotos)
        {
            return Result<Guid>.Failure("property.too_many_photos");
        }

        Property property;
        try
        {
            var location = command.Latitude is { } lat && command.Longitude is { } lng ? new GeoPoint(lat, lng) : null;
            var address = new Address(neighborhoodId, location, command.Landmark, command.Details);
            property = new Property(account.Id, command.Kind, command.Name ?? string.Empty, address, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        foreach (var photo in command.Photos)
        {
            var stored = await uploader.StoreAsync(photo, MediaKind.Photo, command.UserId, account.Id, cancellationToken);
            if (!stored.IsSuccess)
            {
                return Result<Guid>.Failure(stored.Error);
            }

            property.AddPhoto(stored.Value.Id);
        }

        db.Properties.Add(property);

        if (account.Kind == AccountKind.Household)
        {
            // The offer follows the kind of home (a villa has its own offer); the rhythm (monthly / yearly) chosen
            // at sign-up is kept.
            var code = command.Kind == PropertyKind.Villa ? VillaPlanCode : HouseholdPlanCode;
            var plan = await db.Plans.FirstOrDefaultAsync(p => p.Code == code && p.IsActive && p.Segment == AccountKind.Household, cancellationToken)
                ?? await db.Plans.FirstAsync(p => p.Code == HouseholdPlanCode, cancellationToken);
            db.Subscriptions.Add(Subscription.Create(account, plan, [property.Id], clock.GetUtcNow(), account.PrefersAnnual && plan.OffersAnnual));
        }
        else
        {
            // A business subscription covers every site, including new ones (same price: it is per company).
            var subscription = await db.Subscriptions
                .Where(s => s.AccountId == account.Id && s.Status != SubscriptionStatus.Cancelled)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (subscription is not null)
            {
                subscription.AddProperty(property.Id);
            }
            else if (account.ChosenPlanCode is { } chosen
                && await db.Plans.FirstOrDefaultAsync(p => p.Code == chosen && p.IsActive && p.Segment == AccountKind.Business, cancellationToken) is { } businessPlan)
            {
                // First site of a company that chose its offer at sign-up: the subscription starts waiting for payment.
                db.Subscriptions.Add(Subscription.Create(account, businessPlan, [property.Id], clock.GetUtcNow()));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(property.Id);
    }
}
