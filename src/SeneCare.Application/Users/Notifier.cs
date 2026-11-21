using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Users;

/// <summary>
/// Template keys of the notifications (texts in the resources: Notif_{key with _}_Title / _Text). Who receives
/// what: <see cref="Requests.RequestNotifications"/> (requests, missions, diagnostics) and the payment, sign-up
/// handlers.
/// </summary>
public static class NotificationTemplates
{
    public const string PaymentDeclared = "payment.declared";
    public const string PaymentVerified = "payment.verified";
    public const string PaymentRejected = "payment.rejected";

    /// <summary>Super-admins and support: a new customer finished signing up.</summary>
    public const string AccountCreated = "account.created";

    /// <summary>Dispatchers: a request to analyse (new, or approved by the company).</summary>
    public const string RequestNew = "request.new";

    /// <summary>Company managers: an employee's request waits for their approval.</summary>
    public const string RequestAwaitingApproval = "request.awaiting_approval";

    /// <summary>Customer: SENECARE proposes time slots, one to choose.</summary>
    public const string RequestSlotsProposed = "request.slots_proposed";

    /// <summary>Dispatchers: the customer chose a slot, a technician to assign.</summary>
    public const string RequestSlotChosen = "request.slot_chosen";

    /// <summary>Customer: a technician is assigned (name, date).</summary>
    public const string RequestTechnicianAssigned = "request.technician_assigned";

    public const string RequestOnTheWay = "request.on_the_way";
    public const string RequestOnSite = "request.on_site";

    /// <summary>Customer: work done, to validate and rate.</summary>
    public const string RequestCompleted = "request.completed";

    /// <summary>Technician: the customer validated (score).</summary>
    public const string RequestValidated = "request.validated";

    /// <summary>Dispatchers and the technician: the customer disputes the work.</summary>
    public const string RequestDisputed = "request.disputed";

    /// <summary>Customer: the dispute is taken up again by SENECARE.</summary>
    public const string RequestReopened = "request.reopened";

    public const string RequestCancelled = "request.cancelled";

    /// <summary>Technician: a new intervention mission.</summary>
    public const string MissionAssigned = "mission.assigned";

    /// <summary>Technician: a quarterly diagnostic to carry out.</summary>
    public const string DiagnosticAssigned = "diagnostic.assigned";

    /// <summary>Company managers: the diagnostic report is available.</summary>
    public const string DiagnosticValidated = "diagnostic.validated";
}

/// <summary>
/// Tells people what happened: an in-app notification (the bell) and, for key moments, a short WhatsApp
/// message — SMS when they did not accept WhatsApp. Rows are added to the caller's unit of work; messages
/// are best effort and never fail the business action. The person who acted is never notified of their own action.
/// </summary>
public sealed partial class Notifier(IApplicationDbContext db, IWhatsAppSender whatsApp, ISmsSender sms, TimeProvider clock, ILogger<Notifier> logger)
{
    /// <summary>Finance and super-admins: something to check in the back office.</summary>
    public Task ToFinanceAsync(string template, object payload, CancellationToken cancellationToken = default) =>
        ToRolesAsync([PlatformRole.Finance, PlatformRole.SuperAdmin], template, payload, exceptUserId: null, cancellationToken);

    /// <summary>Every active SENECARE staff member holding one of <paramref name="roles"/>.</summary>
    public async Task ToRolesAsync(
        IReadOnlyCollection<PlatformRole> roles, string template, object payload, Guid? exceptUserId, CancellationToken cancellationToken = default)
    {
        var staff = await db.Users
            .Where(u => u.IsActive && u.Roles.Any(r => roles.Contains(r.Role)))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        foreach (var userId in staff.Where(id => id != exceptUserId))
        {
            Add(userId, template, payload);
        }
    }

    /// <summary>The household owner / company managers: in the app, and by message when <paramref name="message"/> is given.</summary>
    public async Task ToAccountAsync(Guid accountId, string template, object payload, string? message, CancellationToken cancellationToken = default)
    {
        var managers = await AccountMembersAsync(accountId, [AccountRole.Owner, AccountRole.Manager], cancellationToken);
        await ToUsersAsync(managers, template, payload, message, exceptUserId: null, cancellationToken);
    }

    /// <summary>Members of a customer account holding one of <paramref name="roles"/>.</summary>
    public async Task<IReadOnlyList<Guid>> AccountMembersAsync(Guid accountId, IReadOnlyCollection<AccountRole> roles, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        return account is null ? [] : [.. account.Members.Where(m => roles.Contains(m.Role)).Select(m => m.UserId)];
    }

    /// <summary>Given people (active, not anonymised): in the app, and by message when <paramref name="message"/> is given.</summary>
    public async Task ToUsersAsync(
        IEnumerable<Guid> userIds, string template, object payload, string? message, Guid? exceptUserId, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => id != exceptUserId).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var users = await db.Users.Where(u => ids.Contains(u.Id) && u.IsActive && u.AnonymizedAt == null).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            Add(user.Id, template, payload);
            if (message is not null)
            {
                await SendAsync(user, message, cancellationToken);
            }
        }
    }

    private void Add(Guid userId, string template, object payload) =>
        db.Notifications.Add(new Notification(userId, NotificationChannel.InApp, template, JsonSerializer.Serialize(payload), isCritical: false, clock.GetUtcNow()));

    private async Task SendAsync(User user, string message, CancellationToken cancellationToken)
    {
        try
        {
            if (user.Consents.Any(c => c.Type == ConsentType.WhatsAppMessages && c.IsActive))
            {
                await whatsApp.SendAsync(user.PhoneNumber, message, cancellationToken);
            }
            else
            {
                await sms.SendAsync(user.PhoneNumber, message, cancellationToken);
            }
        }
#pragma warning disable CA1031 // A message that cannot leave must not undo the business action.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification message could not be sent")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}
