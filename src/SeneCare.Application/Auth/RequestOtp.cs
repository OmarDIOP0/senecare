using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SeneCare.Application.Abstractions;
using SeneCare.Application.Common;
using SeneCare.Application.Resources;
using SeneCare.Domain.Common;
using SeneCare.Domain.Users;

namespace SeneCare.Application.Auth;

public sealed record RequestOtpCommand(string? Phone, OtpChannel Channel, string? IpAddress);

/// <param name="DevelopmentCode">Only with a development switch of <see cref="OtpOptions"/>: the code itself.</param>
public sealed record OtpRequested(Guid ChallengeId, PhoneNumber Phone, OtpChannel Channel, DateTimeOffset ExpiresAt, DateTimeOffset ResendAvailableAt, string? DevelopmentCode = null);

/// <summary>
/// Sends a sign-in code. Never reveals whether the number already has an account.
/// Limits: cooldown between codes, codes per number per hour, codes per IP per hour.
/// </summary>
public sealed partial class RequestOtpHandler(
    IApplicationDbContext db,
    ISecretHasher hasher,
    ISmsSender sms,
    IWhatsAppSender whatsApp,
    IOptions<OtpOptions> options,
    TimeProvider clock,
    ILogger<RequestOtpHandler> logger)
{
    public async Task<Result<OtpRequested>> HandleAsync(RequestOtpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!PhoneNumber.TryParse(command.Phone, out var phone))
        {
            return Result<OtpRequested>.Failure("phone.invalid");
        }

        var now = clock.GetUtcNow();
        var limits = options.Value;
        var hourAgo = now.AddHours(-1);

        var recentForPhone = await db.OtpChallenges
            .Where(c => c.PhoneNumber == phone && c.CreatedAt > hourAgo)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        if (recentForPhone.Count > 0 && recentForPhone[0] + limits.ResendCooldown > now)
        {
            return new AppError("otp.cooldown", recentForPhone[0] + limits.ResendCooldown);
        }

        if (recentForPhone.Count >= limits.MaxPerPhonePerHour)
        {
            return new AppError("otp.too_many_requests", recentForPhone[^1].AddHours(1));
        }

        if (command.IpAddress is not null)
        {
            var recentForIp = await db.OtpChallenges.CountAsync(c => c.IpAddress == command.IpAddress && c.CreatedAt > hourAgo, cancellationToken);
            if (recentForIp >= limits.MaxPerIpPerHour)
            {
                LogIpThrottled(logger, command.IpAddress);
                return new AppError("otp.too_many_requests", now.AddHours(1));
            }
        }

        // Test switches never reveal the code of a staff account (unless explicitly allowed, on a developer machine).
        var revealCode = (limits.SkipVerification || limits.ShowCodeOnPage)
            && (limits.IncludeStaff || !await db.Users.AnyAsync(u => u.PhoneNumber == phone && u.Roles.Any(r => r.Role != PlatformRole.Customer), cancellationToken));

        var code = Secrets.NumericCode(OtpChallenge.CodeLength);
        var challenge = OtpChallenge.Issue(phone, hasher.Hash(code), command.Channel, command.IpAddress, now);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(cancellationToken);

        if (limits.SkipVerification && revealCode)
        {
            return Result<OtpRequested>.Success(new OtpRequested(challenge.Id, phone, command.Channel, challenge.ExpiresAt, now + limits.ResendCooldown, code));
        }

        var language = await db.Users.Where(u => u.PhoneNumber == phone).Select(u => (Language?)u.Language).FirstOrDefaultAsync(cancellationToken)
            ?? Language.French;
        await SendAsync(phone, command.Channel, Messages.Otp(code, language), cancellationToken);

        return Result<OtpRequested>.Success(new OtpRequested(
            challenge.Id, phone, command.Channel, challenge.ExpiresAt, now + limits.ResendCooldown, limits.ShowCodeOnPage && revealCode ? code : null));
    }

    private async Task SendAsync(PhoneNumber phone, OtpChannel channel, string message, CancellationToken cancellationToken)
    {
        if (channel == OtpChannel.WhatsApp)
        {
            try
            {
                await whatsApp.SendAsync(phone, message, cancellationToken);
                return;
            }
#pragma warning disable CA1031 // Any WhatsApp failure must fall back to SMS.
            catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogWhatsAppFallback(logger, ex);
            }
        }

        await sms.SendAsync(phone, message, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OTP throttled for IP {IpAddress}")]
    private static partial void LogIpThrottled(ILogger logger, string ipAddress);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WhatsApp OTP failed, falling back to SMS")]
    private static partial void LogWhatsAppFallback(ILogger logger, Exception exception);
}
