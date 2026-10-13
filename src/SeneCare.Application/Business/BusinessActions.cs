using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Requests;

namespace SeneCare.Application.Business;

/// <summary>
/// Optional approval circuit: an employee (« Employé déclarant ») reports, the site manager or the
/// manager approves (the SLA starts then) or refuses with a reason the employee sees.
/// </summary>
public sealed class RequestApprovalHandler(IApplicationDbContext db, BusinessAccess access, TimeProvider clock)
{
    public async Task<Result<Guid>> ApproveAsync(Guid userId, Guid requestId, CancellationToken cancellationToken = default)
    {
        var (request, error) = await LoadAsync(userId, requestId, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure(error!);
        }

        var planId = await db.Subscriptions
            .Where(s => s.CoveredPropertyIds.Contains(request.PropertyId))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.PlanId)
            .FirstAsync(cancellationToken);
        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == planId, cancellationToken);
        try
        {
            request.Approve(userId, plan.GetMaxDelay(request.Urgency), clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }

    public async Task<Result<Guid>> RejectAsync(Guid userId, Guid requestId, string? reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<Guid>.Failure("business.reject_reason");
        }

        var (request, error) = await LoadAsync(userId, requestId, cancellationToken);
        if (request is null)
        {
            return Result<Guid>.Failure(error!);
        }

        try
        {
            request.Cancel(userId, reason, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(request.Id);
    }

    private async Task<(ServiceRequest? Request, string? Error)> LoadAsync(Guid userId, Guid requestId, CancellationToken cancellationToken)
    {
        var member = await access.OfAsync(userId, cancellationToken);
        var request = await db.ServiceRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (member is null || request is null || request.AccountId != member.Account.Id || !member.CanAccess(request.PropertyId))
        {
            return (null, "request.not_found");
        }

        if (!member.CanApprove)
        {
            return (null, "business.approver_only");
        }

        return request.Status == RequestStatus.AwaitingApproval ? (request, null) : (null, "business.not_awaiting_approval");
    }
}

/// <summary>Manager's settings of the company account: team roles and sites, approval circuit.</summary>
public sealed class TeamManagementHandler(IApplicationDbContext db, BusinessAccess access, TimeProvider clock)
{
    public async Task<Result<bool>> SetApprovalAsync(Guid managerUserId, bool required, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(managerUserId, cancellationToken);
        if (member is not { IsManager: true })
        {
            return Result<bool>.Failure("business.manager_only");
        }

        member.Account.SetRequestApproval(required);
        Audit(managerUserId, "account.approval", member.Account, required ? "on" : "off");
        await db.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(required);
    }

    /// <summary>Changes a member's sub-role and sites. Managers see every site, so their list is ignored.</summary>
    public async Task<Result<Guid>> UpdateMemberAsync(
        Guid managerUserId, Guid memberUserId, AccountRole role, IReadOnlyCollection<Guid> siteIds, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(managerUserId, cancellationToken);
        if (member is not { IsManager: true })
        {
            return Result<Guid>.Failure("business.manager_only");
        }

        var accountSites = await access.SiteIdsAsync(member, cancellationToken);
        if (siteIds.Any(id => !accountSites.Contains(id)))
        {
            return Result<Guid>.Failure("business.site_unknown");
        }

        if (role != AccountRole.Manager && siteIds.Count == 0)
        {
            return Result<Guid>.Failure("business.site_required");
        }

        try
        {
            member.Account.ChangeMemberRole(memberUserId, role, role == AccountRole.Manager ? [] : siteIds);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        Audit(managerUserId, "account.member_update", member.Account, $"{memberUserId} → {role}");
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(memberUserId);
    }

    public async Task<Result<Guid>> RemoveMemberAsync(Guid managerUserId, Guid memberUserId, CancellationToken cancellationToken = default)
    {
        var member = await access.OfAsync(managerUserId, cancellationToken);
        if (member is not { IsManager: true })
        {
            return Result<Guid>.Failure("business.manager_only");
        }

        try
        {
            member.Account.RemoveMember(memberUserId);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        Audit(managerUserId, "account.member_remove", member.Account, memberUserId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(memberUserId);
    }

    private void Audit(Guid userId, string action, CustomerAccount account, string details) =>
        db.AuditLogs.Add(new AuditLog(userId, action, nameof(CustomerAccount), account.Id.ToString(), false, null, details, clock.GetUtcNow()));
}
