using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Resources;
using SeneCare.Application.Technicians;
using SeneCare.Domain.Common;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Technicians;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Development;

public enum SimulationStep
{
    /// <summary>Dispatcher analyses and proposes three slots.</summary>
    ProposeSlots = 1,

    /// <summary>A demo technician is assigned and leaves.</summary>
    OnTheWay = 2,

    /// <summary>Arrives and starts working.</summary>
    StartWork = 3,

    /// <summary>Closes the intervention with a report, an « after » photo and the warranty.</summary>
    Complete = 4,

    /// <summary>
    /// Confirms the first proposed slot and gives the mission to the demo technician (70 000 00 01),
    /// who then plays it for real in the technician app.
    /// </summary>
    Assign = 5,
}

/// <summary>
/// DEVELOPMENT ONLY (the Web refuses it elsewhere). Plays the dispatcher and technician roles so the
/// customer journey (slot choice, technician contact, validation…) can be tried before Phases 8–9
/// build the real back-office and technician app.
/// </summary>
public sealed class RequestSimulator(IApplicationDbContext db, IFileStorage storage, Settings settings, AssignmentHandler assignments, TimeProvider clock)
{
    private static readonly PhoneNumber DemoTechnicianPhone = PhoneNumber.Parse("70 000 00 01");

    // 1×1 PNG, stands for the technician's « after » photo.
    private static readonly byte[] DemoPhoto = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    public async Task<Result<Guid>> RunAsync(Guid requestId, SimulationStep step, CancellationToken cancellationToken = default)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure("request.not_found");
        }

        var now = clock.GetUtcNow();
        try
        {
            switch (step)
            {
                case SimulationStep.ProposeSlots:
                    if (request.Status == RequestStatus.Submitted)
                    {
                        request.StartReview(Guid.Empty, now);
                    }

                    var tomorrow = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                    request.ProposeAppointment(
                        [
                            new TimeSlot(tomorrow.AddHours(9), tomorrow.AddHours(11)),
                            new TimeSlot(tomorrow.AddHours(15), tomorrow.AddHours(17)),
                            new TimeSlot(tomorrow.AddDays(1).AddHours(10), tomorrow.AddDays(1).AddHours(12)),
                        ],
                        Guid.Empty,
                        now);
                    break;

                case SimulationStep.Assign:
                    if (request.Status == RequestStatus.AppointmentProposed && request.CurrentAppointment is { } proposed)
                    {
                        request.ConfirmAppointment(proposed.ProposedSlots.OrderBy(s => s.Start).First(), request.ReportedByUserId, now);
                    }

                    var assignee = await DemoTechnicianAsync(request.Domain, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    var assigned = await assignments.AssignInterventionAsync(request.Id, assignee.Id, cancellationToken);
                    return assigned.IsSuccess ? Result<Guid>.Success(request.Id) : Result<Guid>.Failure(assigned.Error);

                case SimulationStep.OnTheWay:
                    var technician = await DemoTechnicianAsync(request.Domain, cancellationToken);
                    if (!await db.Interventions.AnyAsync(i => i.RequestId == request.Id && i.CompletedAt == null, cancellationToken))
                    {
                        db.Interventions.Add(new Intervention(request.Id, request.PropertyId, request.Domain, technician.Id, Messages.Checklist(request.Domain, Language.French)));
                    }

                    request.MarkOnTheWay(technician.UserId, now);
                    break;

                case SimulationStep.StartWork:
                    var started = await InterventionOfAsync(request.Id, cancellationToken);
                    started.Start(now);
                    request.StartWork(Guid.Empty, now);
                    break;

                case SimulationStep.Complete:
                    var intervention = await InterventionOfAsync(request.Id, cancellationToken);
                    var key = await storage.SaveAsync(new MemoryStream(DemoPhoto), "png", cancellationToken);
                    var photo = new MediaFile(key, "image/png", DemoPhoto.Length, MediaKind.PhotoAfter, Guid.Empty, request.AccountId, now);
                    db.MediaFiles.Add(photo);
                    intervention.AddPhoto(photo.Id, MediaKind.PhotoAfter);
                    intervention.Complete(
                        "Intervention de démonstration : problème identifié et réparé, zone nettoyée.",
                        "Démonstration (pas de signature)",
                        await settings.IntAsync(SettingKeys.WarrantyDays, cancellationToken),
                        now);
                    request.Complete(Guid.Empty, now);
                    break;
            }
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }

    private async Task<Intervention> InterventionOfAsync(Guid requestId, CancellationToken cancellationToken) =>
        await db.Interventions.Where(i => i.RequestId == requestId && i.CompletedAt == null).FirstOrDefaultAsync(cancellationToken)
        ?? throw new DomainException("simulation.no_intervention", "Send the technician first.");

    internal async Task<Technician> DemoTechnicianAsync(ServiceDomain domain, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == DemoTechnicianPhone, cancellationToken);
        if (user is null)
        {
            user = User.Register(DemoTechnicianPhone, clock.GetUtcNow());
            user.UpdateProfile("Moussa Diallo (démo)", null, Language.French);
            user.GrantRole(PlatformRole.Technician, clock.GetUtcNow());
            db.Users.Add(user);
        }

        var technician = await db.Technicians.FirstOrDefaultAsync(t => t.UserId == user.Id, cancellationToken);
        if (technician is null)
        {
            var communes = await db.Communes.Select(c => c.Id).Take(1).ToListAsync(cancellationToken);
            technician = new Technician(user.Id, Enum.GetValues<ServiceDomain>(), communes);
            db.Technicians.Add(technician);
        }
        else if (!technician.Skills.Contains(domain))
        {
            technician.SetSkills(technician.Skills.Append(domain));
        }

        return technician;
    }
}
