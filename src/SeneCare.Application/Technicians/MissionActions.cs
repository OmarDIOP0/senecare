using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Media;
using SeneCare.Application.Resources;
using SeneCare.Domain.Common;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Technicians;

public enum FieldStep
{
    /// <summary>« En route ».</summary>
    OnTheWay = 1,

    /// <summary>« Sur place »: stops the SLA clock.</summary>
    Arrived = 2,

    /// <summary>« En cours ».</summary>
    Start = 3,
}

/// <summary>
/// Technician's actions on a mission, in one tap each. They are idempotent: the phone may replay an
/// action queued offline after it already reached the server (lost answer), which must change nothing.
/// </summary>
public sealed class MissionActions(
    IApplicationDbContext db,
    TechnicianAccess technicians,
    MediaUploader uploader,
    Settings settings,
    ILivePositions positions,
    TimeProvider clock)
{
    private static readonly RequestStatus[] Order =
    [
        RequestStatus.AppointmentConfirmed, RequestStatus.OnTheWay, RequestStatus.OnSite, RequestStatus.InProgress, RequestStatus.Completed,
    ];

    public async Task<Result<Guid>> StepAsync(Guid userId, Guid interventionId, FieldStep step, CancellationToken cancellationToken = default)
    {
        var mission = await LoadAsync(userId, interventionId, cancellationToken);
        if (mission is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        var (intervention, request) = mission.Value;
        var target = step switch
        {
            FieldStep.OnTheWay => RequestStatus.OnTheWay,
            FieldStep.Arrived => RequestStatus.OnSite,
            _ => RequestStatus.InProgress,
        };
        if (request.Status == RequestStatus.Cancelled)
        {
            return Result<Guid>.Failure("mission.cancelled");
        }

        if (Rank(request.Status) >= Rank(target))
        {
            return Result<Guid>.Success(intervention.Id); // already done (replayed action)
        }

        var now = clock.GetUtcNow();
        try
        {
            switch (step)
            {
                case FieldStep.OnTheWay:
                    request.MarkOnTheWay(userId, now);
                    break;
                case FieldStep.Arrived:
                    request.MarkArrived(userId, now);
                    positions.Clear(request.Id);
                    break;
                default:
                    if (request.Status == RequestStatus.OnTheWay)
                    {
                        request.MarkArrived(userId, now);
                    }

                    request.StartWork(userId, now);
                    intervention.Start(now);
                    positions.Clear(request.Id);
                    break;
            }
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(intervention.Id);
    }

    public Task<Result<Guid>> CheckAsync(Guid userId, Guid interventionId, Guid itemId, bool done, CancellationToken cancellationToken = default) =>
        UpdateAsync(userId, interventionId, i => i.MarkChecklistItem(itemId, done, null), cancellationToken);

    public Task<Result<Guid>> AddChecklistItemAsync(Guid userId, Guid interventionId, string? label, CancellationToken cancellationToken = default) =>
        UpdateAsync(userId, interventionId, i => i.AddChecklistItem(label ?? string.Empty), cancellationToken);

    public Task<Result<Guid>> AddPartAsync(
        Guid userId, Guid interventionId, string? name, decimal quantity, string? unit, CancellationToken cancellationToken = default) =>
        UpdateAsync(userId, interventionId, i => i.AddPart(new PartUsed(name ?? string.Empty, quantity, unit ?? string.Empty, null)), cancellationToken);

    /// <summary>Photos before / after the work (compressed on the phone).</summary>
    public async Task<Result<Guid>> AddPhotosAsync(
        Guid userId, Guid interventionId, MediaKind kind, IReadOnlyList<UploadedFile> photos, CancellationToken cancellationToken = default)
    {
        if (kind is not (MediaKind.PhotoBefore or MediaKind.PhotoAfter))
        {
            return Result<Guid>.Failure("intervention.photo_kind");
        }

        if (photos.Count is 0 or > 6)
        {
            return Result<Guid>.Failure("mission.photos_count");
        }

        var mission = await LoadAsync(userId, interventionId, cancellationToken);
        if (mission is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        var (intervention, request) = mission.Value;
        try
        {
            foreach (var photo in photos)
            {
                var stored = await uploader.StoreAsync(photo, kind, userId, request.AccountId, cancellationToken);
                if (!stored.IsSuccess)
                {
                    return Result<Guid>.Failure(stored.Error);
                }

                intervention.AddPhoto(stored.Value.Id, kind);
            }
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(intervention.Id);
    }

    /// <summary>Customer's signature drawn with the finger (PNG from the canvas).</summary>
    public async Task<Result<Guid>> SignAsync(
        Guid userId, Guid interventionId, UploadedFile? signature, string? signedByName, CancellationToken cancellationToken = default)
    {
        if (signature is null)
        {
            return Result<Guid>.Failure("mission.signature_missing");
        }

        var mission = await LoadAsync(userId, interventionId, cancellationToken);
        if (mission is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        var (intervention, request) = mission.Value;
        if (intervention.IsCompleted)
        {
            return Result<Guid>.Success(intervention.Id);
        }

        var stored = await uploader.StoreAsync(signature, MediaKind.Signature, userId, request.AccountId, cancellationToken);
        if (!stored.IsSuccess)
        {
            return Result<Guid>.Failure(stored.Error);
        }

        try
        {
            intervention.SignByCustomer(stored.Value.Id, signedByName ?? string.Empty);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(intervention.Id);
    }

    /// <summary>
    /// « Terminé »: the report is the technician's own words followed by an automatic summary (steps done,
    /// parts, time on site). The warranty starts; the customer is asked to validate.
    /// </summary>
    public async Task<Result<Guid>> CompleteAsync(
        Guid userId, Guid interventionId, string? findings, string? noSignatureReason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(findings))
        {
            return Result<Guid>.Failure("mission.findings_required");
        }

        var mission = await LoadAsync(userId, interventionId, cancellationToken);
        if (mission is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        var (intervention, request) = mission.Value;
        if (intervention.IsCompleted)
        {
            return Result<Guid>.Success(intervention.Id);
        }

        var now = clock.GetUtcNow();
        var language = await db.Users.Where(u => u.Id == request.ReportedByUserId).Select(u => u.Language).FirstAsync(cancellationToken);
        try
        {
            intervention.Complete(
                ComposeReport(findings.Trim(), intervention, now, language),
                noSignatureReason,
                await settings.IntAsync(SettingKeys.WarrantyDays, cancellationToken),
                now);
            request.Complete(userId, now);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(intervention.Id);
    }

    internal static string ComposeReport(string findings, Intervention intervention, DateTimeOffset now, Language language)
    {
        var lines = new List<string> { findings };
        var done = intervention.Checklist.Where(c => c.IsDone).Select(c => c.Label).ToList();
        if (done.Count > 0)
        {
            lines.Add(Messages.ReportDone(string.Join(", ", done), language));
        }

        if (intervention.PartsUsed.Count > 0)
        {
            lines.Add(Messages.ReportParts(
                string.Join(", ", intervention.PartsUsed.Select(p => $"{p.Name} × {p.Quantity.ToString("0.##", CultureInfo.GetCultureInfo("fr"))} {p.Unit}")),
                language));
        }

        if (intervention.StartedAt is { } started)
        {
            lines.Add(Messages.ReportDuration((int)Math.Max(1, (now - started).TotalMinutes), language));
        }

        var report = string.Join("\n", lines);
        return report.Length <= Intervention.ReportMaxLength ? report : report[..Intervention.ReportMaxLength];
    }

    private async Task<Result<Guid>> UpdateAsync(Guid userId, Guid interventionId, Action<Intervention> change, CancellationToken cancellationToken)
    {
        var mission = await LoadAsync(userId, interventionId, cancellationToken);
        if (mission is null)
        {
            return Result<Guid>.Failure("mission.not_found");
        }

        try
        {
            change(mission.Value.Intervention);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(interventionId);
    }

    private async Task<(Intervention Intervention, ServiceRequest Request)?> LoadAsync(Guid userId, Guid interventionId, CancellationToken cancellationToken)
    {
        var technician = await technicians.OfAsync(userId, cancellationToken);
        var intervention = await db.Interventions.FirstOrDefaultAsync(i => i.Id == interventionId, cancellationToken);
        if (technician is null || intervention is null || !intervention.Involves(technician.Id))
        {
            return null;
        }

        var request = await db.ServiceRequests.FirstAsync(r => r.Id == intervention.RequestId, cancellationToken);
        return (intervention, request);
    }

    private static int Rank(RequestStatus status) => Array.IndexOf(Order, status) is var i and >= 0 ? i : int.MaxValue;
}
