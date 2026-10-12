using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Users;

namespace SeneCare.Testing;

public sealed record SentMessage(string Channel, PhoneNumber To, string Text);

/// <summary>Replaces SMS and WhatsApp senders in tests; lets a test read the OTP code that was « sent ».</summary>
public sealed partial class CapturingMessageSender : ISmsSender, IWhatsAppSender
{
    private readonly ConcurrentQueue<SentMessage> _messages = new();

    public IReadOnlyCollection<SentMessage> Messages => [.. _messages];

    Task ISmsSender.SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(new SentMessage("Sms", to, message));
        return Task.CompletedTask;
    }

    Task IWhatsAppSender.SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken)
    {
        _messages.Enqueue(new SentMessage("WhatsApp", to, message));
        return Task.CompletedTask;
    }

    public SentMessage LastFor(PhoneNumber phone) =>
        _messages.Reverse().FirstOrDefault(m => m.To == phone)
        ?? throw new InvalidOperationException($"No message sent to {phone}.");

    /// <summary>The 6-digit code of the last message sent to <paramref name="phone"/>.</summary>
    public string LastCodeFor(PhoneNumber phone) => CodePattern().Match(LastFor(phone).Text).Value;

    /// <summary>The absolute link in the last message sent to <paramref name="phone"/>.</summary>
    public Uri LastLinkFor(PhoneNumber phone) => new(LinkPattern().Match(LastFor(phone).Text).Value);

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex LinkPattern();
}

public static class TestData
{
    /// <summary>Random valid Senegalese mobile number, unique enough for one test run.</summary>
    public static PhoneNumber NewPhone() =>
        PhoneNumber.Parse("77" + RandomNumberGenerator.GetInt32(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Configuration needed by the Web and Api hosts in tests (random keys per run).</summary>
    public static IReadOnlyDictionary<string, string> HostSettings(string connectionString) => new Dictionary<string, string>
    {
        ["ConnectionStrings:Default"] = connectionString,
        ["Security:HashKey"] = HashKey,
        ["Jwt:SigningKey"] = JwtKey,
        ["RateLimits:Otp:PermitLimit"] = "10000",
        ["Database:MigrateOnStartup"] = "false",
        ["Database:SeedOnStartup"] = "false",
        ["Storage:Root"] = Path.Combine(Path.GetTempPath(), "senecare-tests-uploads"),
    };

    private static readonly string HashKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static readonly string JwtKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
