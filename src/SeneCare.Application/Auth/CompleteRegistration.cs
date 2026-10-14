using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Auth;

public static class RegistrationStatus
{
    /// <summary>Version of the privacy policy the user consents to (shown on /confidentialite).</summary>
    public const string PrivacyPolicyVersion = "2026-10";

    /// <summary>Complete = name + personal-data consent, and either an account membership or a staff role.</summary>
    public static bool IsComplete(User user, bool hasAccount)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsProfileComplete && (hasAccount || user.IsStaff);
    }
}

public sealed record CompleteRegistrationCommand(
    Guid UserId,
    string? FullName,
    AccountKind? Profile,
    string? CompanyName,
    bool AcceptsPrivacyPolicy,
    bool AcceptsWhatsApp,
    string? PlanCode = null,
    bool Annual = false);

/// <summary>
/// Last sign-up step: name, « Ma maison » / « Mon entreprise », explicit consent (law 2008-12).
/// Invited employees already belong to an account and skip the profile choice.
/// </summary>
public sealed class CompleteRegistrationHandler(IApplicationDbContext db, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(CompleteRegistrationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!command.AcceptsPrivacyPolicy)
        {
            return Result<Guid>.Failure("registration.consent_required");
        }

        if (string.IsNullOrWhiteSpace(command.FullName))
        {
            return Result<Guid>.Failure("registration.name_required");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result<Guid>.Failure("user.not_found");
        }

        var account = await db.CustomerAccounts.FirstOrDefaultAsync(a => a.Members.Any(m => m.UserId == user.Id), cancellationToken);
        if (account is null && !user.IsStaff)
        {
            switch (command.Profile)
            {
                case AccountKind.Household:
                    account = CustomerAccount.CreateHousehold(command.FullName, user.Id, clock.GetUtcNow());
                    break;
                case AccountKind.Business when string.IsNullOrWhiteSpace(command.CompanyName):
                    return Result<Guid>.Failure("registration.company_required");
                case AccountKind.Business:
                    account = CustomerAccount.CreateBusiness(command.CompanyName, null, user.Id, clock.GetUtcNow());
                    break;
                default:
                    return Result<Guid>.Failure("registration.profile_required");
            }

            // The offer picked during sign-up: used when the first home / site is added.
            if (!string.IsNullOrWhiteSpace(command.PlanCode))
            {
                var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == command.PlanCode && p.IsActive, cancellationToken);
                if (plan is null || plan.Segment != account.Kind)
                {
                    return Result<Guid>.Failure("registration.plan_unknown");
                }

                account.ChooseOffer(plan.Code, command.Annual && plan.OffersAnnual);
            }

            db.CustomerAccounts.Add(account);
        }

        var now = clock.GetUtcNow();
        try
        {
            user.UpdateProfile(command.FullName, user.Email, user.Language);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(ex.Code);
        }

        user.GiveConsent(ConsentType.PersonalData, RegistrationStatus.PrivacyPolicyVersion, now);
        if (command.AcceptsWhatsApp)
        {
            user.GiveConsent(ConsentType.WhatsAppMessages, RegistrationStatus.PrivacyPolicyVersion, now);
        }
        else
        {
            user.WithdrawConsent(ConsentType.WhatsAppMessages, now);
        }

        if (account is not null)
        {
            user.GrantRole(PlatformRole.Customer, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(user.Id);
    }
}
