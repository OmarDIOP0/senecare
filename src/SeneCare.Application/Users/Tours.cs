using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Common;

namespace SeneCare.Application.Users;

/// <summary>Guided tour keys, one per kind of space (the content differs by role).</summary>
public static class TourKeys
{
    public const string Client = "client";
    public const string Business = "entreprise";
    public const string Technician = "technicien";
    public const string Admin = "admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { Client, Business, Technician, Admin };
}

/// <summary>Marks a guided tour as seen (finished or skipped), or resets it to replay it.</summary>
public sealed class TourProgressHandler(IApplicationDbContext db)
{
    public async Task<Result<bool>> CompleteAsync(Guid userId, string? tourKey, CancellationToken cancellationToken = default) =>
        await UpdateAsync(userId, tourKey, complete: true, cancellationToken);

    public async Task<Result<bool>> ResetAsync(Guid userId, string? tourKey, CancellationToken cancellationToken = default) =>
        await UpdateAsync(userId, tourKey, complete: false, cancellationToken);

    public async Task<bool> HasCompletedAsync(Guid userId, string tourKey, CancellationToken cancellationToken = default)
    {
        var tours = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.CompletedTours)
            .FirstOrDefaultAsync(cancellationToken);
        return tours?.Contains(tourKey, StringComparer.Ordinal) ?? false;
    }

    private async Task<Result<bool>> UpdateAsync(Guid userId, string? tourKey, bool complete, CancellationToken cancellationToken)
    {
        if (tourKey is null || !TourKeys.All.Contains(tourKey))
        {
            return Result<bool>.Failure("tour.unknown");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Result<bool>.Failure("user.not_found");
        }

        try
        {
            if (complete)
            {
                user.CompleteTour(tourKey);
            }
            else
            {
                user.ResetTour(tourKey);
            }
        }
        catch (DomainException ex)
        {
            return Result<bool>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
