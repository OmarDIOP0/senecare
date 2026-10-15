using System.Text.Json;
using SeneCare.Domain.Common;

namespace SeneCare.Web.Infrastructure;

/// <summary>
/// Texts of the public site, centralised in <c>Content/site-content.json</c> so they can be edited without
/// touching the views. Blocks marked <c>"sample": true</c> are EXAMPLE CONTENT: shown in development with an
/// « Exemple » label, hidden in production until replaced by real content. Prices of the plans and response
/// times are NOT here: they come from the database (Admin › Offres).
/// </summary>
public sealed class SiteContent
{
    public required IReadOnlyList<Trade> Trades { get; init; }
    public required SampleList<Technician> Technicians { get; init; }
    public required ExampleJob ExampleJob { get; init; }
    public required SampleList<Review> Reviews { get; init; }
    public required SampleList<Figure> Figures { get; init; }
    public required SampleList<Faq> Faq { get; init; }
    public required PlanExtras Plans { get; init; }
    public required IReadOnlyList<string> Neighborhoods { get; init; }

    /// <summary>True in development: sample blocks are shown, labelled. False in production: they are hidden.</summary>
    public bool ShowSamples { get; private set; }

    public bool Visible(ISample block) => !block.Sample || ShowSamples;

    private string _webRoot = string.Empty;

    /// <summary>Photos are named placeholders until the file is dropped in wwwroot (e.g. /images/metier-plomberie.webp).</summary>
    public bool HasImage(string path) =>
        File.Exists(Path.Combine(_webRoot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

    public Trade? TradeBySlug(string slug) => Trades.FirstOrDefault(t => string.Equals(t.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public Trade? TradeFor(ServiceDomain domain) => Trades.FirstOrDefault(t => t.Domain == domain);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static SiteContent Load(string contentRoot, string webRoot, bool showSamples)
    {
        var path = Path.Combine(contentRoot, "Content", "site-content.json");
        var content = JsonSerializer.Deserialize<SiteContent>(File.ReadAllText(path), Options)
            ?? throw new InvalidOperationException($"Empty site content: {path}");
        content.ShowSamples = showSamples;
        content._webRoot = webRoot;
        return content;
    }
}

public interface ISample
{
    bool Sample { get; }
}

public sealed record SampleList<T>(bool Sample, IReadOnlyList<T> Items) : ISample;

public sealed record Trade(
    string Slug, ServiceDomain Domain, string Name, string Summary, string Intro, IReadOnlyList<string> Examples,
    string Delay, string StartingPrice, string Image, bool Sample) : ISample;

public sealed record Technician(string FirstName, string Trade, int Years, string Quote);

public sealed record ExampleJob(bool Sample, string Neighborhood, string Trade, string Problem, string Work, string Duration, string Price) : ISample;

public sealed record Review(string Author, string Neighborhood, DateOnly Date, int Rating, string Trade, string Text);

public sealed record Figure(string Value, string Label);

public sealed record Faq(string Question, string Answer);

public sealed record HouseholdRow(string Label, string OneOff, string Plan);

public sealed record HouseholdExtras(string OneOffLabel, string OneOffPrice, IReadOnlyList<HouseholdRow> Rows);

public sealed record BusinessExtras(string Sites, string Parts, string Contact, string Report);

public sealed record PlanExtras(bool Sample, HouseholdExtras Household, IReadOnlyDictionary<string, BusinessExtras> Business) : ISample;
