using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using SeneCare.Domain.Content;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Caching;

/// <summary>
/// The most read data of the public site, kept in memory: SENECARE's contact details (every page) and the home
/// data (offers, figures, advice, reviews: home, prices, trades, sign-up). Saved in the database → dropped at once
/// by <see cref="PublicCacheInvalidator"/>; otherwise kept a few minutes (figures move with every finished job).
/// </summary>
public static class PublicCache
{
    public const string Contact = "public:contact";
    public const string Home = "public:home";

    public static readonly TimeSpan ContactLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan HomeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>What a save of these types makes stale. Users: an erased person's review must disappear at once.</summary>
    internal static readonly IReadOnlyDictionary<Type, string[]> StaleAfterSaving = new Dictionary<Type, string[]>
    {
        [typeof(AppSetting)] = [Contact, Home],
        [typeof(Plan)] = [Home],
        [typeof(PreventiveAdvice)] = [Home],
        [typeof(Rating)] = [Home],
        [typeof(User)] = [Home],
    };
}

/// <summary>Drops the public cache entries made stale by a successful save (admin edits, new review, erasure).</summary>
public sealed class PublicCacheInvalidator(IMemoryCache cache) : SaveChangesInterceptor
{
    // One context per request, one save at a time on it: keys noted before the save, removed once it succeeded.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<DbContext, HashSet<string>> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Note(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Note(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Drop(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Drop(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Note(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var keys = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .SelectMany(e => PublicCache.StaleAfterSaving.TryGetValue(e.Metadata.ClrType, out var stale) ? stale : [])
            .ToHashSet();
        if (keys.Count > 0)
        {
            _pending.AddOrUpdate(context, keys);
        }
    }

    private void Drop(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var keys))
        {
            return;
        }

        _pending.Remove(context);
        foreach (var key in keys)
        {
            cache.Remove(key);
        }
    }
}
