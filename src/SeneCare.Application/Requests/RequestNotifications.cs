using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Users;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Users;
using Messages = SeneCare.Application.Resources.Messages;
using T = SeneCare.Application.Users.NotificationTemplates;

namespace SeneCare.Application.Requests;

/// <summary>What a save is about to record, as far as notifications are concerned (built by the persistence layer).</summary>
public sealed record NotificationChanges(
    IReadOnlyList<ServiceRequest> NewRequests,
    IReadOnlyList<(ServiceRequest Request, RequestStatus From, RequestStatus To)> StatusChanges,
    IReadOnlyList<Intervention> NewMissions,
    IReadOnlyList<Diagnostic> AssignedDiagnostics,
    IReadOnlyList<Diagnostic> ValidatedDiagnostics)
{
    public bool IsEmpty =>
        NewRequests.Count == 0 && StatusChanges.Count == 0 && NewMissions.Count == 0 && AssignedDiagnostics.Count == 0 && ValidatedDiagnostics.Count == 0;
}

/// <summary>
/// Who hears about what in the life of a request (CLAUDE.md §1, ANOMALIE → … → SUIVI), whatever screen made it
/// happen: called before every save, it adds the notifications to the same unit of work.
/// <list type="bullet">
/// <item>Customer (household owner, company managers and the employee who reported): slots to choose, technician
/// assigned, on the way, on site, work done; employee requests to approve (managers and site managers).</item>
/// <item>Technician: new mission or diagnostic, validation and rating, dispute, cancellation.</item>
/// <item>SENECARE dispatchers (and super-admins): new request, slot chosen, dispute, cancellation by the customer.</item>
/// </list>
/// The person who acted is never notified of their own action.
/// </summary>
public sealed class RequestNotifications(IApplicationDbContext db, Notifier notifier)
{
    private static readonly PlatformRole[] Dispatch = [PlatformRole.Dispatcher, PlatformRole.SuperAdmin];
    private static readonly AccountRole[] Approvers = [AccountRole.Owner, AccountRole.Manager, AccountRole.SiteManager];
    private static readonly AccountRole[] Holders = [AccountRole.Owner, AccountRole.Manager];
    private static readonly AccountRole[] AllRoles = [AccountRole.Owner, AccountRole.Manager, AccountRole.SiteManager, AccountRole.Reporter];

    public async Task HandleAsync(NotificationChanges changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        foreach (var request in changes.NewRequests)
        {
            await NewRequestAsync(request, cancellationToken);
        }

        foreach (var (request, from, to) in changes.StatusChanges)
        {
            await StatusChangedAsync(request, from, to, cancellationToken);
        }

        foreach (var mission in changes.NewMissions)
        {
            await MissionAssignedAsync(mission, cancellationToken);
        }

        foreach (var diagnostic in changes.AssignedDiagnostics)
        {
            var technicianUser = await TechnicianUserAsync(diagnostic.TechnicianId, cancellationToken);
            if (technicianUser is { } userId)
            {
                await notifier.ToUsersAsync(
                    [userId], T.DiagnosticAssigned,
                    new { name = await AccountNameAsync(diagnostic.AccountId, cancellationToken), date = diagnostic.ScheduledFor, url = $"/technicien/diagnostics/{diagnostic.Id}" },
                    message: null, exceptUserId: null, cancellationToken);
            }
        }

        foreach (var diagnostic in changes.ValidatedDiagnostics)
        {
            var managers = await notifier.AccountMembersAsync(diagnostic.AccountId, Holders, cancellationToken);
            await notifier.ToUsersAsync(
                managers, T.DiagnosticValidated, new { url = $"/entreprise/diagnostics/{diagnostic.Id}" }, message: null, exceptUserId: null, cancellationToken);
        }
    }

    private async Task NewRequestAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        if (request.Status == RequestStatus.AwaitingApproval)
        {
            var approvers = await notifier.AccountMembersForPropertyAsync(request.AccountId, request.PropertyId, Approvers, cancellationToken);
            await notifier.ToUsersAsync(
                approvers, T.RequestAwaitingApproval, Payload(request, "/entreprise/demandes"), message: null, request.ReportedByUserId, cancellationToken);
        }
        else
        {
            await ToDispatchAsync(request, T.RequestNew, request.ReportedByUserId, cancellationToken);
        }
    }

    private async Task StatusChangedAsync(ServiceRequest request, RequestStatus from, RequestStatus to, CancellationToken cancellationToken)
    {
        var actor = request.History.LastOrDefault(h => h.To == to)?.ByUserId;
        var customerUrl = $"/interventions/{request.Id}";
        switch (to)
        {
            case RequestStatus.Submitted when from == RequestStatus.AwaitingApproval:
                await ToDispatchAsync(request, T.RequestNew, actor, cancellationToken);
                break;

            case RequestStatus.AppointmentProposed:
                await ToCustomerAsync(request, T.RequestSlotsProposed, Payload(request, customerUrl), Messages.RequestSlotsProposed(request.Number, Language.French), actor, cancellationToken);
                break;

            // Chosen by the customer: a technician to assign. Set by SENECARE (« now »): the assignment tells everybody.
            case RequestStatus.AppointmentConfirmed when await IsCustomerAsync(request, actor, cancellationToken):
                await ToDispatchAsync(request, T.RequestSlotChosen, actor, cancellationToken);
                break;

            case RequestStatus.OnTheWay:
                var technician = await LeadTechnicianAsync(request.Id, cancellationToken);
                await ToCustomerAsync(
                    request, T.RequestOnTheWay, Payload(request, customerUrl, technician?.Name),
                    Messages.RequestOnTheWay(request.Number, technician?.Name ?? string.Empty, Language.French), actor, cancellationToken);
                break;

            case RequestStatus.OnSite:
                await ToCustomerAsync(request, T.RequestOnSite, Payload(request, customerUrl, (await LeadTechnicianAsync(request.Id, cancellationToken))?.Name), null, actor, cancellationToken);
                break;

            case RequestStatus.Completed:
                await ToCustomerAsync(request, T.RequestCompleted, Payload(request, customerUrl), Messages.RequestCompleted(request.Number, Language.French), actor, cancellationToken);
                break;

            case RequestStatus.ValidatedByCustomer:
                await ToTechniciansAsync(request, T.RequestValidated, request.Rating?.Score, actor, cancellationToken);
                break;

            case RequestStatus.Disputed:
                await ToDispatchAsync(request, T.RequestDisputed, actor, cancellationToken);
                await ToTechniciansAsync(request, T.RequestDisputed, null, actor, cancellationToken);
                break;

            case RequestStatus.UnderReview when from == RequestStatus.Disputed:
                await ToCustomerAsync(request, T.RequestReopened, Payload(request, customerUrl), null, actor, cancellationToken);
                break;

            case RequestStatus.Cancelled:
                // Cancelled by the customer side (or refused by a company manager): SENECARE and the technician learn
                // it; cancelled by SENECARE: the customer and the technician do.
                if (from != RequestStatus.AwaitingApproval && await IsCustomerAsync(request, actor, cancellationToken))
                {
                    await ToDispatchAsync(request, T.RequestCancelled, actor, cancellationToken);
                }
                else
                {
                    await ToCustomerAsync(request, T.RequestCancelled, Payload(request, customerUrl), null, actor, cancellationToken);
                }

                await ToTechniciansAsync(request, T.RequestCancelled, null, actor, cancellationToken);
                break;
        }
    }

    private async Task MissionAssignedAsync(Intervention mission, CancellationToken cancellationToken)
    {
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == mission.RequestId, cancellationToken);
        var technician = await db.Technicians.AsNoTracking().Where(t => t.Id == mission.LeadTechnicianId)
            .Join(db.Users, t => t.UserId, u => u.Id, (t, u) => new { u.Id, u.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        if (request is null || technician is null)
        {
            return;
        }

        var start = request.Appointments.Where(a => a.ChosenSlot != null).Select(a => a.ChosenSlot!.Start).OrderByDescending(s => s).FirstOrDefault();
        await notifier.ToUsersAsync(
            [technician.Id], T.MissionAssigned,
            new { number = request.Number, domain = request.Domain.ToString(), date = start == default ? (DateTimeOffset?)null : start, url = $"/technicien/missions/{mission.Id}" },
            start == default ? null : Messages.MissionAssigned(request.Number, start, Language.French),
            exceptUserId: null, cancellationToken);
        await ToCustomerAsync(
            request, T.RequestTechnicianAssigned,
            new { number = request.Number, domain = request.Domain.ToString(), name = technician.FullName, date = start == default ? (DateTimeOffset?)null : start, url = $"/interventions/{request.Id}" },
            null, actor: null, cancellationToken);
    }

    private static object Payload(ServiceRequest request, string url, string? name = null) =>
        new { number = request.Number, domain = request.Domain.ToString(), name, url };

    private Task ToDispatchAsync(ServiceRequest request, string template, Guid? actor, CancellationToken cancellationToken) =>
        notifier.ToRolesAsync(Dispatch, template, Payload(request, $"/admin/dispatch/{request.Id}"), actor, cancellationToken);

    /// <summary>Household owner or company managers, and the employee who reported it while still a member who may see the building.</summary>
    private async Task ToCustomerAsync(ServiceRequest request, string template, object payload, string? message, Guid? actor, CancellationToken cancellationToken)
    {
        var people = (await notifier.AccountMembersAsync(request.AccountId, Holders, cancellationToken)).ToList();
        var members = await notifier.AccountMembersForPropertyAsync(request.AccountId, request.PropertyId, AllRoles, cancellationToken);
        if (members.Contains(request.ReportedByUserId))
        {
            people.Add(request.ReportedByUserId);
        }

        await notifier.ToUsersAsync(people, template, payload, message, actor, cancellationToken);
    }

    /// <summary>The technicians of the request's latest mission (lead and team), towards their mission page.</summary>
    private async Task ToTechniciansAsync(ServiceRequest request, string template, int? score, Guid? actor, CancellationToken cancellationToken)
    {
        var missions = await db.Interventions.AsNoTracking().Where(i => i.RequestId == request.Id).ToListAsync(cancellationToken);
        var mission = missions.OrderByDescending(m => m.CompletedAt == null).ThenByDescending(m => m.CompletedAt).FirstOrDefault();
        if (mission is null)
        {
            return;
        }

        var technicianIds = mission.TeamTechnicianIds.Append(mission.LeadTechnicianId).ToList();
        var users = await db.Technicians.AsNoTracking().Where(t => technicianIds.Contains(t.Id)).Select(t => t.UserId).ToListAsync(cancellationToken);
        await notifier.ToUsersAsync(
            users, template, new { number = request.Number, domain = request.Domain.ToString(), score, url = $"/technicien/missions/{mission.Id}" },
            message: null, actor, cancellationToken);
    }

    private async Task<bool> IsCustomerAsync(ServiceRequest request, Guid? actor, CancellationToken cancellationToken) =>
        actor is { } userId && (userId == request.ReportedByUserId
            || (await notifier.AccountMembersAsync(request.AccountId, AllRoles, cancellationToken)).Contains(userId));

    private async Task<(Guid UserId, string? Name)?> LeadTechnicianAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var lead = await db.Interventions.AsNoTracking().Where(i => i.RequestId == requestId && i.CompletedAt == null)
            .Join(db.Technicians, i => i.LeadTechnicianId, t => t.Id, (i, t) => t.UserId)
            .Join(db.Users, id => id, u => u.Id, (id, u) => new { u.Id, u.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        return lead is null ? null : (lead.Id, lead.FullName);
    }

    private async Task<Guid?> TechnicianUserAsync(Guid? technicianId, CancellationToken cancellationToken) =>
        technicianId is null ? null
            : await db.Technicians.AsNoTracking().Where(t => t.Id == technicianId).Select(t => (Guid?)t.UserId).FirstOrDefaultAsync(cancellationToken);

    private async Task<string?> AccountNameAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.CustomerAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.DisplayName).FirstOrDefaultAsync(cancellationToken);
}
