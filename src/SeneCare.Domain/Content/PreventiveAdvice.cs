using SeneCare.Domain.Common;

namespace SeneCare.Domain.Content;

/// <summary>
/// « Conseil préventif » shown on the customer home, written by SENECARE (e.g. clean the gutters before
/// the hivernage). Months restrict it to a season; none = all year round.
/// </summary>
public sealed class PreventiveAdvice : AggregateRoot
{
    public const int TitleMaxLength = 120;
    public const int BodyMaxLength = 600;

    private readonly List<int> _months = [];

    private PreventiveAdvice()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    public PreventiveAdvice(string title, string body, IEnumerable<int> months, ServiceDomain? domain, DateTimeOffset now)
    {
        Title = string.Empty;
        Body = string.Empty;
        Update(title, body, months, domain, now);
    }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public ServiceDomain? Domain { get; private set; }

    public IReadOnlyCollection<int> Months => _months.AsReadOnly();

    public bool IsPublished { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string title, string body, IEnumerable<int> months, ServiceDomain? domain, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(months);
        var list = months.Distinct().Order().ToList();
        if (list.Exists(m => m is < 1 or > 12))
        {
            throw new DomainException("advice.months", "Months go from 1 to 12.");
        }

        Title = Guard.Required(title, "advice.title", TitleMaxLength);
        Body = Guard.Required(body, "advice.body", BodyMaxLength);
        Domain = domain;
        _months.Clear();
        _months.AddRange(list);
        UpdatedAt = now;
    }

    public void Publish(DateTimeOffset now)
    {
        IsPublished = true;
        UpdatedAt = now;
    }

    public void Unpublish(DateTimeOffset now)
    {
        IsPublished = false;
        UpdatedAt = now;
    }

    public bool AppliesTo(int month) => _months.Count == 0 || _months.Contains(month);
}
