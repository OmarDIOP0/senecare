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

    /// <summary>
    /// Development only: no code is sent, the number is signed in right away. The web app refuses to
    /// start with this switch outside the Development environment.
    /// </summary>
    public bool SkipVerification { get; set; }

    /// <summary>
    /// Development only: the code is still sent and asked for, and also shown under the six boxes so it can be
    /// typed without a phone. Refused at startup outside Development, like <see cref="SkipVerification"/>.
    /// </summary>
    public bool ShowCodeOnPage { get; set; }

    /// <summary>
    /// With the switches above, also for SENECARE staff (admins, finance, technicians). Off on a public test site:
    /// anyone could otherwise sign in as the admin by typing its number. Staff then read their code in the server
    /// log (Console sender), which only the host's owner can see.
    /// </summary>
    public bool IncludeStaff { get; set; }

    public bool IsDevelopmentOnly => SkipVerification || ShowCodeOnPage;
}
