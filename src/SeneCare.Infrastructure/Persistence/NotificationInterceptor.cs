using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Requests;
using SeneCare.Domain.Common;
using SeneCare.Domain.Diagnostics;
using SeneCare.Domain.Interventions;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;

namespace SeneCare.Infrastructure.Persistence;

/// <summary>
/// Before each save: what happened to requests, missions and diagnostics (domain events, new rows) becomes
/// notifications in the same unit of work (<see cref="RequestNotifications"/>), whichever screen or job made it
/// happen. After the save: the new notifications are pushed to the open pages (<see cref="ILiveNotifications"/>).
/// One per context (scoped): the rules are resolved lazily, they use this very context.
/// </summary>
public sealed class NotificationInterceptor(IServiceProvider services) : SaveChangesInterceptor
{
    private bool _running;
    private List<LiveNotification> _pending = [];

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context && !_running)
        {
            var changes = Collect(context);
            if (!changes.IsEmpty)
            {
                _running = true;
                try
                {
                    await services.GetRequiredService<RequestNotifications>().HandleAsync(changes, cancellationToken);
                }
                finally
                {
                    _running = false;
                }
            }

            // Every new in-app notification of this save (requests, payments, sign-ups…), pushed once saved.
            _pending = [.. context.ChangeTracker.Entries<Notification>()
                .Where(e => e.State == EntityState.Added && e.Entity.Channel == NotificationChannel.InApp)
                .Select(e => new LiveNotification(e.Entity.UserId, e.Entity.TemplateKey, e.Entity.PayloadJson))];
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var pending = _pending;
        _pending = [];
        if (pending.Count > 0 && services.GetService<ILiveNotifications>() is { } live)
        {
            await live.PublishAsync(pending, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = [];
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private static NotificationChanges Collect(DbContext context)
    {
        var tracker = context.ChangeTracker;
        var newRequests = tracker.Entries<ServiceRequest>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        var statusChanges = tracker.Entries<ServiceRequest>()
            .SelectMany(e => e.Entity.DomainEvents.OfType<ServiceRequestStatusChanged>().Select(ev => (e.Entity, ev.From, ev.To)))
            .ToList();
        var newMissions = tracker.Entries<Intervention>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        var assigned = tracker.Entries<Diagnostic>()
            .Where(e => e.State == EntityState.Modified && e.Property(d => d.TechnicianId).IsModified && e.Entity.TechnicianId is not null
                && !Equals(e.Property(d => d.TechnicianId).OriginalValue, e.Entity.TechnicianId))
            .Select(e => e.Entity)
            .ToList();
        var validated = tracker.Entries<Diagnostic>().Where(e => e.Entity.DomainEvents.OfType<DiagnosticValidated>().Any()).Select(e => e.Entity).ToList();

        // Handled once: a later save in the same request must not notify again.
        foreach (var aggregate in tracker.Entries<AggregateRoot>().Select(e => e.Entity).Where(a => a.DomainEvents.Count > 0))
        {
            aggregate.ClearDomainEvents();
        }

        return new NotificationChanges(newRequests, statusChanges, newMissions, assigned, validated);
    }
}
