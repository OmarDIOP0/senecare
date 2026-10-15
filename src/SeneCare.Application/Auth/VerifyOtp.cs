using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Auth;

public sealed record VerifyOtpCommand(Guid ChallengeId, string? Code);

public sealed record OtpVerified(Guid UserId, bool IsNewUser, bool IsRegistrationComplete);

/// <summary>Checks the code; on success signs up the number if it is new (account details come next).</summary>
public sealed partial class VerifyOtpHandler(
    IApplicationDbContext db,
    ISecretHasher hasher,
    IIdentityService identity,
    TimeProvider clock,
    ILogger<VerifyOtpHandler> logger)
{
    public async Task<Result<OtpVerified>> HandleAsync(VerifyOtpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = new string((command.Code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        var challenge = await db.OtpChallenges.FirstOrDefaultAsync(c => c.Id == command.ChallengeId, cancellationToken);
        if (challenge is null)
        {
            LogRefused(logger, command.ChallengeId, "not_found");
            return Result<OtpVerified>.Failure("otp.not_found");
        }

        var now = clock.GetUtcNow();
        var matches = code.Length == OtpChallenge.CodeLength && hasher.Matches(code, challenge.CodeHash);
        var result = challenge.Verify(matches, now);
        await db.SaveChangesAsync(cancellationToken);

        if (result != OtpVerificationResult.Success)
        {
            LogRefused(logger, challenge.Id, result.ToString());
        }

        switch (result)
        {
            case OtpVerificationResult.InvalidCode:
                return Result<OtpVerified>.Failure("otp.invalid");
            case OtpVerificationResult.TooManyAttempts:
                return Result<OtpVerified>.Failure("otp.too_many_attempts");
            case OtpVerificationResult.Expired:
                return Result<OtpVerified>.Failure("otp.expired");
            case OtpVerificationResult.AlreadyUsed:
                return Result<OtpVerified>.Failure("otp.already_used");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == challenge.PhoneNumber, cancellationToken);
        var isNew = user is null;
        if (user is null)
        {
            user = User.Register(challenge.PhoneNumber, now);
            db.Users.Add(user);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!user.IsActive)
        {
            return Result<OtpVerified>.Failure("user.inactive");
        }

        await identity.EnsureIdentityUserAsync(user, cancellationToken);

        var hasAccount = await db.CustomerAccounts.AnyAsync(a => a.Members.Any(m => m.UserId == user.Id), cancellationToken);
        return Result<OtpVerified>.Success(new OtpVerified(user.Id, isNew, RegistrationStatus.IsComplete(user, hasAccount)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OTP refused for challenge {ChallengeId}: {Reason}")]
    private static partial void LogRefused(ILogger logger, Guid challengeId, string reason);
}
