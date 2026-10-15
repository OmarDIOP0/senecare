using SeneCare.Domain.Common;

namespace SeneCare.Domain.Users;

public enum OtpChannel
{
    Sms = 1,
    WhatsApp = 2,
}

public enum OtpVerificationResult
{
    Success = 1,
    InvalidCode = 2,
    Expired = 3,
    TooManyAttempts = 4,
    AlreadyUsed = 5,
}

/// <summary>
/// One-time code sent by SMS/WhatsApp to sign in. Only a keyed hash of the code is stored.
/// Valid <see cref="Lifetime"/> minutes, at most <see cref="MaxAttempts"/> tries, single use.
/// </summary>
public sealed class OtpChallenge : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public const int MaxAttempts = 5;
    public const int CodeLength = 6;

    private OtpChallenge()
    {
        PhoneNumber = null!;
        CodeHash = string.Empty;
    }

    private OtpChallenge(PhoneNumber phoneNumber, string codeHash, OtpChannel channel, string? ipAddress, DateTimeOffset now)
    {
        PhoneNumber = phoneNumber;
        CodeHash = codeHash;
        Channel = channel;
        IpAddress = ipAddress;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public PhoneNumber PhoneNumber { get; private set; }

    public string CodeHash { get; private set; }

    public OtpChannel Channel { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public static OtpChallenge Issue(PhoneNumber phoneNumber, string codeHash, OtpChannel channel, string? ipAddress, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        return new OtpChallenge(phoneNumber, codeHash, channel, Guard.Optional(ipAddress, "otp.ip", 45), now);
    }

    public bool IsExpired(DateTimeOffset now) => now > ExpiresAt;

    /// <summary>
    /// Records a verification attempt. <paramref name="codeMatches"/> is computed by the caller
    /// with a constant-time comparison of hashes.
    /// </summary>
    /// <summary>
    /// A right code sent again within this delay after it was accepted (form sent twice on a slow network)
    /// counts as accepted: same person, same code, no new access.
    /// </summary>
    public static readonly TimeSpan ResubmitGrace = TimeSpan.FromMinutes(2);

    public OtpVerificationResult Verify(bool codeMatches, DateTimeOffset now)
    {
        if (ConsumedAt is { } consumed)
        {
            return codeMatches && now - consumed <= ResubmitGrace ? OtpVerificationResult.Success : OtpVerificationResult.AlreadyUsed;
        }

        if (IsExpired(now))
        {
            return OtpVerificationResult.Expired;
        }

        if (Attempts >= MaxAttempts)
        {
            return OtpVerificationResult.TooManyAttempts;
        }

        Attempts++;
        if (!codeMatches)
        {
            return Attempts >= MaxAttempts ? OtpVerificationResult.TooManyAttempts : OtpVerificationResult.InvalidCode;
        }

        ConsumedAt = now;
        return OtpVerificationResult.Success;
    }
}
