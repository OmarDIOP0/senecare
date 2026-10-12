using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Requests;

public sealed record CreateRequestCommand(
    Guid UserId,
    Guid? PropertyId,
    ServiceDomain? Domain,
    string? Description,
    Urgency? Urgency,
    IReadOnlyList<TimeSlot> PreferredSlots,
    IReadOnlyList<UploadedFile> Photos,
    UploadedFile? VoiceNote,
    string? ClientReference);

public sealed record RequestCreated(Guid Id, string Number, bool AlreadyExisted);

/// <summary>
/// « Signaler »: checks the subscription, computes the SLA from the plan, flags warranty cases,
/// and is idempotent for forms replayed by the offline queue (same client reference).
/// </summary>
public sealed class CreateRequestHandler(
    IApplicationDbContext db,
    AccessControl access,
    MediaUploader uploader,
    IDocumentNumberGenerator numbers,
    TimeProvider clock)
{
    public const int MaxPreferredSlots = 3;

    public async Task<Result<RequestCreated>> HandleAsync(CreateRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Domain is not { } domain)
        {
            return Result<RequestCreated>.Failure("request.domain_required");
        }

        if (command.Urgency is not { } urgency)
        {
            return Result<RequestCreated>.Failure("request.urgency_required");
        }

        if (string.IsNullOrWhiteSpace(command.Description))
        {
            return Result<RequestCreated>.Failure("request.description");
        }

        if (command.PropertyId is not { } propertyId || await access.PropertyAsync(command.UserId, propertyId, cancellationToken) is not { } property)
        {
            return Result<RequestCreated>.Failure("request.property_required");
        }

        if (!string.IsNullOrWhiteSpace(command.ClientReference))
        {
            var existing = await db.ServiceRequests.AsNoTracking()
                .Where(r => r.AccountId == property.AccountId && r.ClientReference == command.ClientReference)
                .Select(r => new { r.Id, r.Number })
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Result<RequestCreated>.Success(new RequestCreated(existing.Id, existing.Number, AlreadyExisted: true));
            }
        }

        var subscription = await db.Subscriptions
            .Where(s => s.CoveredPropertyIds.Contains(property.Id))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (subscription is null || !subscription.AllowsRequests)
        {
            return Result<RequestCreated>.Failure("request.subscription_inactive");
        }

        if (command.Photos.Count > ServiceRequest.MaxPhotos)
        {
            return Result<RequestCreated>.Failure("request.too_many_photos");
        }

        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var account = await db.CustomerAccounts.FirstAsync(a => a.Id == property.AccountId, cancellationToken);
        var requiresApproval = account.RequiresRequestApproval && account.FindMember(command.UserId)?.Role == AccountRole.Reporter;
        var now = clock.GetUtcNow();

        ServiceRequest request;
        try
        {
            request = ServiceRequest.Create(
                await numbers.NextServiceRequestNumberAsync(cancellationToken),
                account.Id,
                property.Id,
                command.UserId,
                domain,
                command.Description,
                urgency,
                plan.GetMaxDelay(urgency),
                requiresApproval,
                command.PreferredSlots.Take(MaxPreferredSlots),
                now,
                command.ClientReference);
        }
        catch (DomainException ex)
        {
            return Result<RequestCreated>.Failure(ex.Code);
        }

        foreach (var photo in command.Photos)
        {
            var stored = await uploader.StoreAsync(photo, MediaKind.Photo, command.UserId, account.Id, cancellationToken);
            if (!stored.IsSuccess)
            {
                return Result<RequestCreated>.Failure(stored.Error);
            }

            request.AddAttachment(stored.Value.Id, MediaKind.Photo);
        }

        if (command.VoiceNote is { } voice)
        {
            var stored = await uploader.StoreAsync(voice, MediaKind.VoiceNote, command.UserId, account.Id, cancellationToken);
            if (!stored.IsSuccess)
            {
                return Result<RequestCreated>.Failure(stored.Error);
            }

            request.AddAttachment(stored.Value.Id, MediaKind.VoiceNote);
        }

        // Same trade, same building, within warranty: our own work, no extra charge.
        var today = DakarCalendar.Today(clock);
        var warranty = await db.Interventions.AsNoTracking()
            .Where(i => i.PropertyId == property.Id && i.Domain == domain && i.WarrantyEndsOn >= today)
            .OrderByDescending(i => i.WarrantyEndsOn)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (warranty is { } interventionId)
        {
            request.MarkUnderWarranty(interventionId);
        }

        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        return Result<RequestCreated>.Success(new RequestCreated(request.Id, request.Number, AlreadyExisted: false));
    }
}
