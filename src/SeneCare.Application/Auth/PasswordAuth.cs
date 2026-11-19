using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Platform;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Auth;

/// <summary>How people sign in (configuration section « Auth »).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// <see cref="AuthMode.Password"/> (default): phone number + password, no message to pay for.
    /// <see cref="AuthMode.Otp"/>: one-time code by WhatsApp / SMS, kept for when a messaging provider is set up.
    /// </summary>
    public AuthMode Mode { get; set; } = AuthMode.Password;

    public bool UsesOtp => Mode == AuthMode.Otp;
}

public enum AuthMode
{
    Password = 1,
    Otp = 2,
}

/// <summary>Password rules, shared by the sign-up, the change and the admin reset.</summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    /// <summary>Null when acceptable, otherwise the error code.</summary>
    public static string? Check(string? password, PhoneNumber? phone = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return "password.too_short";
        }

        if (password.Length > MaxLength)
        {
            return "password.too_long";
        }

        // The phone number itself is the first thing anyone would try.
        var digits = new string(password.Where(char.IsAsciiDigit).ToArray());
        if (phone is not null && digits.Length == password.Length && phone.NationalNumber.Contains(digits, StringComparison.Ordinal))
        {
            return "password.is_phone";
        }

        return password.Distinct().Count() < 3 ? "password.too_simple" : null;
    }

    /// <summary>Readable temporary password (no 0/O, 1/l/I) for an admin reset, given to the person by WhatsApp.</summary>
    public static string Temporary()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Concat(Enumerable.Range(0, 10).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]));
    }
}

public sealed record PasswordSignInCommand(string? Phone, string? Password);

public sealed record PasswordSignedIn(Guid UserId, bool MustChangePassword);

/// <summary>
/// Phone number + password. The same error whatever went wrong (unknown number or wrong password), so the form
/// does not tell who has an account; five mistakes lock the account for 15 minutes.
/// </summary>
public sealed class PasswordSignInHandler(IApplicationDbContext db, IIdentityService identity)
{
    public async Task<Result<PasswordSignedIn>> HandleAsync(PasswordSignInCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Phone))
        {
            return Result<PasswordSignedIn>.Failure("phone.required");
        }

        if (!PhoneNumber.TryParse(command.Phone, out var phone))
        {
            return Result<PasswordSignedIn>.Failure("phone.invalid");
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            return Result<PasswordSignedIn>.Failure("password.required");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        if (user is null)
        {
            return Result<PasswordSignedIn>.Failure("auth.invalid");
        }

        if (!user.IsActive)
        {
            return Result<PasswordSignedIn>.Failure("user.inactive");
        }

        return await identity.CheckPasswordAsync(user.Id, command.Password, cancellationToken) switch
        {
            PasswordCheck.Success => Result<PasswordSignedIn>.Success(new PasswordSignedIn(user.Id, false)),
            PasswordCheck.SuccessMustChange => Result<PasswordSignedIn>.Success(new PasswordSignedIn(user.Id, true)),
            PasswordCheck.LockedOut => Result<PasswordSignedIn>.Failure("auth.locked"),
            _ => Result<PasswordSignedIn>.Failure("auth.invalid"),
        };
    }
}

public sealed record PasswordSignUpCommand(string? Phone, string? Password, string? ConfirmPassword);

/// <summary>
/// « Créer mon compte », step 2: the number and a password. A number that already has an account is refused
/// (sign in instead); an abandoned sign-up of a customer (no password, never finished) can be taken up again.
/// Staff numbers never get a password this way (theirs comes from the configuration or an admin).
/// </summary>
public sealed class PasswordSignUpHandler(IApplicationDbContext db, IIdentityService identity, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(PasswordSignUpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Phone))
        {
            return Result<Guid>.Failure("phone.required");
        }

        if (!PhoneNumber.TryParse(command.Phone, out var phone))
        {
            return Result<Guid>.Failure("phone.invalid");
        }

        if (PasswordRules.Check(command.Password, phone) is { } problem)
        {
            return Result<Guid>.Failure(problem);
        }

        if (!string.Equals(command.Password, command.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result<Guid>.Failure("password.mismatch");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        if (user is not null)
        {
            var hasAccount = await db.CustomerAccounts.AnyAsync(a => a.Members.Any(m => m.UserId == user.Id), cancellationToken);
            if (user.IsStaff || hasAccount || !user.IsActive || await identity.HasPasswordAsync(user.Id, cancellationToken))
            {
                return Result<Guid>.Failure("auth.phone_taken");
            }
        }
        else
        {
            user = User.Register(phone, clock.GetUtcNow());
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        await identity.EnsureIdentityUserAsync(user, cancellationToken);
        var set = await identity.SetPasswordAsync(user.Id, command.Password!, mustChange: false, cancellationToken);
        return set is null ? Result<Guid>.Success(user.Id) : Result<Guid>.Failure(set);
    }
}

public sealed record ChangePasswordCommand(Guid UserId, string? CurrentPassword, string? NewPassword, string? ConfirmPassword);

/// <summary>« Changer mon mot de passe »; after an admin reset, the temporary one is not asked again.</summary>
public sealed class ChangePasswordHandler(IApplicationDbContext db, IIdentityService identity)
{
    public async Task<Result<Guid>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);
        if (user is null)
        {
            return Result<Guid>.Failure("user.not_found");
        }

        var mustChange = await identity.MustChangePasswordAsync(user.Id, cancellationToken);
        if (!mustChange && await identity.HasPasswordAsync(user.Id, cancellationToken)
            && await identity.CheckPasswordAsync(user.Id, command.CurrentPassword ?? string.Empty, cancellationToken) is not (PasswordCheck.Success or PasswordCheck.SuccessMustChange))
        {
            return Result<Guid>.Failure("password.current_wrong");
        }

        if (PasswordRules.Check(command.NewPassword, user.PhoneNumber) is { } problem)
        {
            return Result<Guid>.Failure(problem);
        }

        if (!string.Equals(command.NewPassword, command.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result<Guid>.Failure("password.mismatch");
        }

        var set = await identity.SetPasswordAsync(user.Id, command.NewPassword!, mustChange: false, cancellationToken);
        return set is null ? Result<Guid>.Success(user.Id) : Result<Guid>.Failure(set);
    }
}

/// <summary>
/// « Mot de passe oublié », done by SENECARE: a temporary password, shown once to the admin who gives it to the
/// person (WhatsApp, phone); it must be changed at the next sign-in. Logged as access to sensitive data.
/// </summary>
public sealed class ResetPasswordHandler(IApplicationDbContext db, IIdentityService identity, TimeProvider clock)
{
    public async Task<Result<string>> HandleAsync(Guid adminUserId, Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || user.AnonymizedAt is not null)
        {
            return Result<string>.Failure("user.not_found");
        }

        // Staff passwords are not reset from the customers screens.
        if (user.IsStaff)
        {
            return Result<string>.Failure("password.staff");
        }

        await identity.EnsureIdentityUserAsync(user, cancellationToken);
        var temporary = PasswordRules.Temporary();
        var set = await identity.SetPasswordAsync(user.Id, temporary, mustChange: true, cancellationToken);
        if (set is not null)
        {
            return Result<string>.Failure(set);
        }

        db.AuditLogs.Add(new AuditLog(adminUserId, "user.password_reset", nameof(User), user.Id.ToString(), true, null, null, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result<string>.Success(temporary);
    }
}
