using SeneCare.Domain.Common;

namespace SeneCare.Domain.Platform;

public enum NotificationChannel
{
    WhatsApp = 1,
    Sms = 2,
    Push = 3,
    Email = 4,
    InApp = 5,
}

public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Read = 4,
}

/// <summary>A message to a user on one channel. Fallback (WhatsApp → SMS) creates a new notification.</summary>
public sealed class Notification : AggregateRoot
{
    private Notification()
    {
        TemplateKey = string.Empty;
        PayloadJson = string.Empty;
    }

    public Notification(Guid userId, NotificationChannel channel, string templateKey, string payloadJson, bool isCritical, DateTimeOffset now)
    {
        UserId = userId;
        Channel = channel;
        TemplateKey = Guard.Required(templateKey, "notification.template", 100);
        PayloadJson = payloadJson ?? "{}";
        IsCritical = isCritical;
        Status = NotificationStatus.Pending;
        CreatedAt = now;
    }

    public Guid UserId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public string TemplateKey { get; private set; }

    public string PayloadJson { get; private set; }

    /// <summary>Critical notifications ignore quiet hours (22 h – 7 h).</summary>
    public bool IsCritical { get; private set; }

    public NotificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkSent(DateTimeOffset now)
    {
        Attempts++;
        Status = NotificationStatus.Sent;
        SentAt = now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        Attempts++;
        Status = NotificationStatus.Failed;
        LastError = Guard.Optional(error, "notification.error", 1000);
    }

    public void MarkRead(DateTimeOffset now)
    {
        Status = NotificationStatus.Read;
        ReadAt ??= now;
    }
}

/// <summary>Immutable trace of a sensitive action (access to personal data, manual payment…). Law 2008-12 / CDP.</summary>
public sealed class AuditLog : Entity
{
    private AuditLog()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public AuditLog(
        Guid? actorUserId, string action, string entityType, string? entityId, bool isSensitiveData, string? ipAddress, string? details, DateTimeOffset at)
    {
        ActorUserId = actorUserId;
        Action = Guard.Required(action, "audit.action", 100);
        EntityType = Guard.Required(entityType, "audit.entity", 100);
        EntityId = Guard.Optional(entityId, "audit.entity_id", 64);
        IsSensitiveData = isSensitiveData;
        IpAddress = Guard.Optional(ipAddress, "audit.ip", 45);
        Details = Guard.Optional(details, "audit.details", 4000);
        At = at;
    }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public bool IsSensitiveData { get; private set; }

    public string? IpAddress { get; private set; }

    public string? Details { get; private set; }

    public DateTimeOffset At { get; private set; }
}

/// <summary>Admin-editable business parameter (grace period, thresholds…).</summary>
public sealed class AppSetting
{
    private AppSetting()
    {
        Key = string.Empty;
        Value = string.Empty;
    }

    public AppSetting(string key, string value, DateTimeOffset now)
    {
        Key = Guard.Required(key, "setting.key", 100);
        Value = value ?? string.Empty;
        UpdatedAt = now;
    }

    public string Key { get; private set; }

    public string Value { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Change(string value, DateTimeOffset now)
    {
        Value = value ?? string.Empty;
        UpdatedAt = now;
    }
}

/// <summary>Keys and default values of <see cref="AppSetting"/>.</summary>
public static class SettingKeys
{
    public const string PaymentGraceDays = "billing.grace_days";
    public const string AutoValidateAfterDays = "requests.auto_validate_after_days";
    public const string WarrantyDays = "interventions.warranty_days";
    public const string NeighborhoodThreshold = "neighborhoods.default_threshold";

    /// <summary>Wave merchant payment link (« {montant} » is replaced by the amount when present). Empty = not configured.</summary>
    public const string WaveLink = "payments.wave_link";

    /// <summary>Public contact details (site header, footer, call / WhatsApp buttons). Empty = not shown.</summary>
    public const string ContactPhone = "contact.phone";
    public const string ContactWhatsApp = "contact.whatsapp";
    public const string ContactEmail = "contact.email";
    public const string ContactAddress = "contact.address";
    public const string ContactHours = "contact.hours";
    public const string ContactAreas = "contact.areas";
    public const string ContactFacebook = "contact.facebook";
    public const string ContactInstagram = "contact.instagram";
    public const string ContactLinkedIn = "contact.linkedin";

    public static readonly IReadOnlyList<string> Contact =
    [
        ContactPhone, ContactWhatsApp, ContactEmail, ContactAddress, ContactHours, ContactAreas,
        ContactFacebook, ContactInstagram, ContactLinkedIn,
    ];

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PaymentGraceDays] = "7",
        [AutoValidateAfterDays] = "3",
        [WarrantyDays] = "90",
        [NeighborhoodThreshold] = "500",
        [WaveLink] = "",
        // SENECARE's number given by the owner; the rest is filled in Admin › Paramètres.
        [ContactPhone] = "762919145",
        [ContactWhatsApp] = "762919145",
        [ContactEmail] = "",
        [ContactAddress] = "",
        [ContactHours] = "",
        [ContactAreas] = "",
        [ContactFacebook] = "",
        [ContactInstagram] = "",
        [ContactLinkedIn] = "",
    };
}
