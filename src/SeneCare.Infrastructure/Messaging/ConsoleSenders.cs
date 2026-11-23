using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SeneCare.Application.Abstractions;
using SeneCare.Domain.Users;

namespace SeneCare.Infrastructure.Messaging;

public sealed class MessagingOptions
{
    public const string SmsSection = "Sms";
    public const string WhatsAppSection = "WhatsApp";

    /// <summary>
    /// « Console » (development: message written to the logs), « None » (production without a provider yet: nothing
    /// is sent, the in-app notification remains) or a real provider (Phase 11).
    /// </summary>
    public string Provider { get; set; } = "Console";
}

/// <summary>Development SMS: the message (including OTP codes) is written to the application log.</summary>
internal sealed partial class ConsoleSmsSender : ISmsSender
{
    private readonly ILogger<ConsoleSmsSender> _logger;

    public ConsoleSmsSender(ILogger<ConsoleSmsSender> logger, IHostEnvironment environment)
    {
        ConsoleGuard.EnsureNotProduction(environment);
        _logger = logger;
    }

    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(to);
        LogMessage(_logger, to.Formatted, message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "[SMS → {Phone}] {Message}")]
    private static partial void LogMessage(ILogger logger, string phone, string message);
}

/// <summary>Development WhatsApp: same as <see cref="ConsoleSmsSender"/>.</summary>
internal sealed partial class ConsoleWhatsAppSender : IWhatsAppSender
{
    private readonly ILogger<ConsoleWhatsAppSender> _logger;

    public ConsoleWhatsAppSender(ILogger<ConsoleWhatsAppSender> logger, IHostEnvironment environment)
    {
        ConsoleGuard.EnsureNotProduction(environment);
        _logger = logger;
    }

    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(to);
        LogMessage(_logger, to.Formatted, message);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "[WhatsApp → {Phone}] {Message}")]
    private static partial void LogMessage(ILogger logger, string phone, string message);
}

/// <summary>
/// Production before a message provider is chosen (user decision: password sign-in, no SMS / WhatsApp costs): the
/// message is not sent and never written anywhere (no phone number, no content in the logs).
/// </summary>
internal sealed partial class NoMessageSender(ILogger<NoMessageSender> logger) : ISmsSender, IWhatsAppSender
{
    public Task SendAsync(PhoneNumber to, string message, CancellationToken cancellationToken = default)
    {
        LogSkipped(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message not sent: no SMS / WhatsApp provider configured")]
    private static partial void LogSkipped(ILogger logger);
}

internal static class ConsoleGuard
{
    /// <summary>Console senders write OTP codes to the logs: never acceptable in production.</summary>
    public static void EnsureNotProduction(IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            throw new InvalidOperationException("The Console SMS/WhatsApp provider cannot be used in Production. Configure Sms:Provider.");
        }
    }
}
