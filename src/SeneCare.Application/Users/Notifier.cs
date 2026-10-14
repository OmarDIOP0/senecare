using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Users;

/// <summary>Template keys of the notifications (texts in the resources: Notif_{key with _}_Title / _Text).</summary>
public static class NotificationTemplates
{
    public const string PaymentDeclared = "payment.declared";
    public const string PaymentVerified = "payment.verified";
    public const string PaymentRejected = "payment.rejected";
}

/// <summary>
/// Tells people what happened: an in-app notification (the bell) and, for customers, a short WhatsApp
/// message — SMS when they did not accept WhatsApp. Rows are added to the caller's unit of work; messages
/// are best effort and never fail the business action.
/// </summary>
public sealed partial class Notifier(IApplicationDbContext db, IWhatsAppSender whatsApp, ISmsSender sms, TimeProvider clock, ILogger<Notifier> logger)
{
    /// <summary>Finance and super-admins: something to check in the back office.</summary>
    public async Task ToFinanceAsync(string template, object payload, CancellationToken cancellationToken = default)
    {
        var staff = await db.Users
            .Where(u => u.IsActive && u.Roles.Any(r => r.Role == PlatformRole.Finance || r.Role == PlatformRole.SuperAdmin))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
        foreach (var userId in staff)
        {
            Add(userId, template, payload);
        }
    }

    /// <summary>The household owner / company managers: in the app, and by message when <paramref name="message"/> is given.</summary>
    public async Task ToAccountAsync(Guid accountId, string template, object payload, string? message, CancellationToken cancellationToken = default)
    {
        var account = await db.CustomerAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var managers = account.Members.Where(m => m.Role is AccountRole.Owner or AccountRole.Manager).Select(m => m.UserId).ToList();
        var users = await db.Users.Where(u => managers.Contains(u.Id) && u.IsActive && u.AnonymizedAt == null).ToListAsync(cancellationToken);
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
#pragma warning disable CA1031 // A message that cannot leave must not undo the payment decision.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification message could not be sent")]
    private static partial void LogSendFailed(ILogger logger, Exception exception);
}
