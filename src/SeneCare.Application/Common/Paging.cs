using Microsoft.EntityFrameworkCore;

namespace SeneCare.Application.Common;

/// <summary>One page of a list (« Précédent / Suivant »), numbered from 1.</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Number, bool HasNext)
{
    public bool HasPrevious => Number > 1;

    public int Count => Items.Count;
}

/// <summary>Lists that grow with the activity are read one page at a time, never all at once.</summary>
public static class Paging
{
    public const int Size = 50;

    public static int Clamp(int page) => Math.Clamp(page, 1, 10_000);

    /// <summary>One more row than the page tells whether a next page exists, without a COUNT(*).</summary>
    public static async Task<Page<T>> ToPageAsync<T>(this IQueryable<T> query, int page, CancellationToken cancellationToken, int size = Size)
    {
        ArgumentNullException.ThrowIfNull(query);
        var number = Clamp(page);
        var rows = await query.Skip((number - 1) * size).Take(size + 1).ToListAsync(cancellationToken);
        return new Page<T>([.. rows.Take(size)], number, rows.Count > size);
    }

    /// <summary>
    /// A page of a list already read and ordered in memory: for lists bounded by nature (active dispatch queue, a
    /// company's sites, the SENECARE team) whose order is computed after reading.
    /// </summary>
    public static Page<T> Of<T>(IReadOnlyList<T> all, int page, int size = Size)
    {
        ArgumentNullException.ThrowIfNull(all);
        var number = Clamp(page);
        return new Page<T>([.. all.Skip((number - 1) * size).Take(size)], number, all.Count > number * size);
    }

    public static Page<TOut> Map<TIn, TOut>(this Page<TIn> page, IReadOnlyList<TOut> items)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new Page<TOut>(items, page.Number, page.HasNext);
    }
}
