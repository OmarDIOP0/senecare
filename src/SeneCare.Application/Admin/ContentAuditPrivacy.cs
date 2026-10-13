using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Billing;
using SeneCare.Domain.Common;
using SeneCare.Domain.Content;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Admin;

// ===================================================================== Preventive advice

public sealed record AdviceForm(string? Title, string? Body, IReadOnlyCollection<int> Months, ServiceDomain? Domain);

/// <summary>Preventive advice written by SENECARE for the customer home (seasonal: hivernage, harmattan…).</summary>
public sealed class AdviceManagement(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<PreventiveAdvice>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.PreventiveAdvices.AsNoTracking().OrderByDescending(a => a.UpdatedAt).ToListAsync(cancellationToken);

    public async Task<Result<Guid>> CreateAsync(Guid adminId, AdviceForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        PreventiveAdvice advice;
        try
        {
            advice = new PreventiveAdvice(form.Title ?? string.Empty, form.Body ?? string.Empty, form.Months, form.Domain, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.PreventiveAdvices.Add(advice);
        db.AuditLogs.Add(new AuditLog(adminId, "content.advice_create", nameof(PreventiveAdvice), advice.Id.ToString(), false, null, advice.Title, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(advice.Id);
    }

    public Task<Result<Guid>> UpdateAsync(Guid adminId, Guid adviceId, AdviceForm form, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, adviceId, "content.advice_update", a => a.Update(form.Title ?? string.Empty, form.Body ?? string.Empty, form.Months, form.Domain, clock.GetUtcNow()), cancellationToken);

    public Task<Result<Guid>> SetPublishedAsync(Guid adminId, Guid adviceId, bool published, CancellationToken cancellationToken = default) =>
        ChangeAsync(adminId, adviceId, published ? "content.advice_publish" : "content.advice_unpublish", a =>
        {
            if (published)
            {
                a.Publish(clock.GetUtcNow());
            }
            else
            {
                a.Unpublish(clock.GetUtcNow());
            }
        }, cancellationToken);

    /// <summary>The advice of the month for the customer home: most recently updated published one that applies.</summary>
    public async Task<PreventiveAdvice?> ForMonthAsync(int month, CancellationToken cancellationToken = default) =>
        (await db.PreventiveAdvices.AsNoTracking().Where(a => a.IsPublished).OrderByDescending(a => a.UpdatedAt).ToListAsync(cancellationToken))
            .FirstOrDefault(a => a.AppliesTo(month));

    private async Task<Result<Guid>> ChangeAsync(Guid adminId, Guid adviceId, string action, Action<PreventiveAdvice> change, CancellationToken cancellationToken)
    {
        var advice = await db.PreventiveAdvices.FirstOrDefaultAsync(a => a.Id == adviceId, cancellationToken);
        if (advice is null)
        {
            return Result<Guid>.Failure("advice.not_found");
        }

        try
        {
            change(advice);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        db.AuditLogs.Add(new AuditLog(adminId, action, nameof(PreventiveAdvice), advice.Id.ToString(), false, null, advice.Title, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(advice.Id);
    }
}

// ===================================================================== Audit log

public sealed record AuditRow(DateTimeOffset At, string? Actor, string Action, string EntityType, string? EntityId, bool IsSensitive, string? IpAddress, string? Details);

/// <summary>Who did what, when (law 2008-12: accesses to personal data are flagged « sensitive »).</summary>
public sealed class AuditQuery(IApplicationDbContext db)
{
    public const int PageSize = 100;

    public async Task<IReadOnlyList<AuditRow>> ListAsync(string? action, bool sensitiveOnly, int page, CancellationToken cancellationToken = default)
    {
        var term = action?.Trim() ?? string.Empty;
        return await (
                from a in db.AuditLogs
                join u in db.Users on a.ActorUserId equals u.Id into actors
                from u in actors.DefaultIfEmpty()
                where (term == string.Empty || a.Action.StartsWith(term)) && (!sensitiveOnly || a.IsSensitiveData)
                orderby a.At descending
                select new AuditRow(a.At, u == null ? null : u.FullName, a.Action, a.EntityType, a.EntityId, a.IsSensitiveData, a.IpAddress, a.Details))
            .AsNoTracking()
            .Skip(Math.Max(0, page) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);
    }
}

// ===================================================================== Personal data (law 2008-12 / CDP)

public sealed record ErasureResult(int Accounts, int Requests, int Files);

public sealed record PersonalDataExport(
    DateTimeOffset GeneratedAt,
    object Person,
    IReadOnlyList<object> Consents,
    IReadOnlyList<object> Accounts,
    IReadOnlyList<object> Buildings,
    IReadOnlyList<object> Requests,
    IReadOnlyList<object> Invoices,
    IReadOnlyList<object> Payments);

/// <summary>
/// Right of access: everything SENECARE holds about one person, as a JSON file. Each export is written
/// in the audit log as a sensitive access (who exported whose data, when, from where).
/// </summary>
public sealed class PersonalDataService(IApplicationDbContext db, IIdentityService identity, IFileStorage storage, TimeProvider clock)
{
    public const string RedactedText = "[supprimé]";

    public async Task<PersonalDataExport?> ExportAsync(Guid adminId, Guid userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var accounts = await db.CustomerAccounts.AsNoTracking().Where(a => a.Members.Any(m => m.UserId == userId)).ToListAsync(cancellationToken);
        var accountIds = accounts.Select(a => a.Id).ToList();
        var properties = await db.Properties.AsNoTracking().Where(p => accountIds.Contains(p.AccountId)).ToListAsync(cancellationToken);
        var requests = await db.ServiceRequests.AsNoTracking().Where(r => r.ReportedByUserId == userId).ToListAsync(cancellationToken);
        var invoices = await db.Invoices.AsNoTracking().Where(i => accountIds.Contains(i.AccountId)).ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => invoiceIds.Contains(p.InvoiceId)).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();

        db.AuditLogs.Add(new AuditLog(adminId, "privacy.export", nameof(User), userId.ToString(), true, ipAddress, null, now));
        await db.SaveChangesAsync(cancellationToken);

        return new PersonalDataExport(
            now,
            new { user.Id, Phone = user.PhoneNumber.E164, user.FullName, user.Email, Language = user.Language.ToString(), user.CreatedAt, user.IsActive, Roles = user.Roles.Select(r => r.Role.ToString()) },
            [.. user.Consents.Select(c => (object)new { Type = c.Type.ToString(), c.PolicyVersion, c.GivenAt, c.WithdrawnAt })],
            [.. accounts.Select(a => (object)new { a.Id, Kind = a.Kind.ToString(), a.DisplayName, a.Ninea, a.CreatedAt, Role = a.FindMember(userId)!.Role.ToString() })],
            [.. properties.Select(p => (object)new { p.Id, p.Name, Kind = p.Kind.ToString(), p.Address.Landmark, p.Address.Details, p.Address.Location?.Latitude, p.Address.Location?.Longitude, p.CreatedAt })],
            [.. requests.Select(r => (object)new { r.Number, Domain = r.Domain.ToString(), r.Description, Urgency = r.Urgency.ToString(), Status = r.Status.ToString(), r.CreatedAt, Rating = r.Rating?.Score, RatingComment = r.Rating?.Comment })],
            [.. invoices.Select(i => (object)new { i.Number, Total = i.Total.Amount, Status = i.Status.ToString(), i.DueDate, i.PaidAt })],
            [.. payments.Select(p => (object)new { Amount = p.Amount.Amount, Channel = p.Channel.ToString(), Status = p.Status.ToString(), p.CreatedAt, p.CustomerReference })]);
    }

    /// <summary>
    /// Right to erasure, by anonymisation: invoices and payments are kept (legal obligation) but no longer
    /// point to a person. A household erased alone loses its homes' names, addresses and photos; in a
    /// company, the person simply leaves the team (a last manager must hand over first). Staff accounts are
    /// handled by HR, not here. Irreversible, audit-logged.
    /// </summary>
    public async Task<Result<ErasureResult>> EraseAsync(Guid adminId, Guid userId, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Result<ErasureResult>.Failure("privacy.not_found");
        }

        if (user.AnonymizedAt is not null)
        {
            return Result<ErasureResult>.Failure("privacy.already_erased");
        }

        if (user.IsStaff)
        {
            return Result<ErasureResult>.Failure("privacy.staff");
        }

        var now = clock.GetUtcNow();
        var accounts = await db.CustomerAccounts.Where(a => a.Members.Any(m => m.UserId == userId)).ToListAsync(cancellationToken);
        var households = new List<Guid>();
        foreach (var account in accounts)
        {
            if (account.Members.Count == 1)
            {
                households.Add(account.Id);
                continue;
            }

            try
            {
                account.RemoveMember(userId);
            }
            catch (DomainException ex)
            {
                return Result<ErasureResult>.Failure(ex.Code == "account.last_admin" ? "privacy.last_manager" : ex.Code);
            }
        }

        // Accounts the person held alone: the buildings and their history stop describing anyone.
        foreach (var account in accounts.Where(a => households.Contains(a.Id)))
        {
            account.Rename(RedactedText);
        }

        var properties = await db.Properties.Where(p => households.Contains(p.AccountId)).ToListAsync(cancellationToken);
        foreach (var property in properties)
        {
            property.Update(property.Kind, RedactedText, new Address(property.Address.NeighborhoodId, null, null, null));
            foreach (var photo in property.Photos.ToList())
            {
                property.RemovePhoto(photo.MediaFileId);
            }
        }

        var subscriptions = await db.Subscriptions.Where(s => households.Contains(s.AccountId)).ToListAsync(cancellationToken);
        subscriptions.ForEach(s => s.Cancel(now));

        var requests = await db.ServiceRequests
            .Where(r => r.ReportedByUserId == userId || households.Contains(r.AccountId))
            .ToListAsync(cancellationToken);
        foreach (var request in requests)
        {
            if (Domain.Requests.RequestStateMachine.CanTransition(request.Status, Domain.Requests.RequestStatus.Cancelled))
            {
                request.Cancel(adminId, null, now);
            }

            request.Redact(RedactedText);
        }

        // Photos, voice notes, signatures: what they uploaded, and everything about their own homes.
        var media = await db.MediaFiles
            .Where(m => m.UploadedByUserId == userId || (m.AccountId != null && households.Contains(m.AccountId.Value)))
            .Select(m => m.StorageKey)
            .ToListAsync(cancellationToken);
        foreach (var key in media)
        {
            await storage.DeleteAsync(key, cancellationToken);
        }

        user.Anonymize(await PlaceholderAsync(cancellationToken), now);
        db.AuditLogs.Add(new AuditLog(adminId, "privacy.erase", nameof(User), userId.ToString(), true, ipAddress, null, now));
        await db.SaveChangesAsync(cancellationToken);
        await identity.ForgetAsync(user, cancellationToken);
        return Result<ErasureResult>.Success(new ErasureResult(households.Count, requests.Count, media.Count));
    }

    private async Task<PhoneNumber> PlaceholderAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = PhoneNumber.Anonymized(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000));
            if (!await db.Users.AnyAsync(u => u.PhoneNumber == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No free placeholder number.");
    }
}
