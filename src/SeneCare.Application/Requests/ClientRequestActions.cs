using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Requests;

/// <summary>What the customer does on a request: pick a slot, validate with a rating, dispute, cancel.</summary>
public sealed class ClientRequestActions(IApplicationDbContext db, AccessControl access, TimeProvider clock)
{
    /// <summary>Chooses one of the proposed slots (identified by its start time).</summary>
    public Task<Result<Guid>> ChooseSlotAsync(Guid userId, Guid requestId, DateTimeOffset slotStart, CancellationToken cancellationToken = default) =>
        RunAsync(userId, requestId, request =>
        {
            var slot = request.CurrentAppointment?.ProposedSlots.FirstOrDefault(s => s.Start == slotStart)
                ?? throw new DomainException("appointment.unknown_slot", "Choose one of the proposed slots.");
            request.ConfirmAppointment(slot, userId, clock.GetUtcNow());
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Validates the finished work with a 1–5 rating; the technician's average is updated.</summary>
    public Task<Result<Guid>> ValidateAsync(Guid userId, Guid requestId, int score, string? comment, CancellationToken cancellationToken = default) =>
        RunAsync(userId, requestId, async request =>
        {
            var now = clock.GetUtcNow();
            request.ValidateByCustomer(userId, new Rating(score, comment, now), now);

            var technician = await (
                    from i in db.Interventions
                    join t in db.Technicians on i.LeadTechnicianId equals t.Id
                    where i.RequestId == request.Id
                    select t)
                .FirstOrDefaultAsync(cancellationToken);
            technician?.AddRating(score);
        }, cancellationToken);

    public Task<Result<Guid>> DisputeAsync(Guid userId, Guid requestId, string? reason, CancellationToken cancellationToken = default) =>
        RunAsync(userId, requestId, request =>
        {
            request.Dispute(userId, reason ?? string.Empty, clock.GetUtcNow());
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<Result<Guid>> CancelAsync(Guid userId, Guid requestId, string? reason, CancellationToken cancellationToken = default) =>
        RunAsync(userId, requestId, request =>
        {
            request.Cancel(userId, reason, clock.GetUtcNow());
            return Task.CompletedTask;
        }, cancellationToken);

    private async Task<Result<Guid>> RunAsync(Guid userId, Guid requestId, Func<ServiceRequest, Task> action, CancellationToken cancellationToken)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null || await access.PropertyAsync(userId, request.PropertyId, cancellationToken) is null)
        {
            return Result<Guid>.Failure("request.not_found");
        }

        try
        {
            await action(request);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }
}
