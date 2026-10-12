namespace SeneCare.Application.Auth;

/// <summary>Anti-abuse limits for OTP sending (configuration section « Otp »).</summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Minimum delay between two codes for the same number.</summary>
    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Max codes per phone number per hour (SMS cost and harassment protection).</summary>
    public int MaxPerPhonePerHour { get; set; } = 5;

    /// <summary>Max codes requested from one IP address per hour.</summary>
    public int MaxPerIpPerHour { get; set; } = 20;
}
