namespace SeneCare.Domain.Common;

/// <summary>Trade covered by SENECARE (« domaine d'intervention »).</summary>
public enum ServiceDomain
{
    Electricity = 1,
    Plumbing = 2,
    Waterproofing = 3,
    Masonry = 4,
    Carpentry = 5,
    Security = 6,
    Outdoor = 7,
}

/// <summary>Urgency of a request. Max response delays are configured per plan (<c>PlanSlaPolicy</c>).</summary>
public enum Urgency
{
    /// <summary>Power cut, short circuit, major leak — default target under 4 h.</summary>
    Critical = 1,

    /// <summary>Default target under 24 h.</summary>
    High = 2,

    /// <summary>Default target under 72 h.</summary>
    Normal = 3,

    /// <summary>Scheduled work, no SLA.</summary>
    Planned = 4,
}

public enum MediaKind
{
    Photo = 1,
    PhotoBefore = 2,
    PhotoAfter = 3,
    VoiceNote = 4,
    Signature = 5,
    Document = 6,
}

public enum Language
{
    French = 1,
    Wolof = 2,
    English = 3,
}
