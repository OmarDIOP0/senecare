namespace SeneCare.Domain.Common;

/// <summary>Raised when a business rule is violated. <see cref="Code"/> is a stable key for UI messages.</summary>
public class DomainException : Exception
{
    private const string DefaultCode = "domain.error";

    public DomainException()
        : this(DefaultCode, "A business rule was violated.")
    {
    }

    public DomainException(string message)
        : this(DefaultCode, message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = DefaultCode;

    public DomainException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
