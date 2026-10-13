using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SeneCare.Domain.Accounts;
using SeneCare.Domain.Common;
using SeneCare.Domain.Locations;
using SeneCare.Domain.Neighborhoods;
using SeneCare.Domain.Plans;
using SeneCare.Domain.Platform;

namespace SeneCare.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seeding of reference data: Senegal locations, the four offers and default settings.
/// Only inserts what is missing — never overwrites values edited by admins.
/// </summary>
public sealed partial class DatabaseSeeder(SeneCareDbContext db, TimeProvider clock, ILogger<DatabaseSeeder> logger)
{
    private const string LocationsResource = "SeneCare.Infrastructure.Persistence.Seed.senegal-locations.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedLocationsAsync(cancellationToken);
        await SeedPlansAsync(cancellationToken);
        await SeedSettingsAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SeedNeighborhoodProgramsAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The four offers from the specification. Prices are then managed by admins.</summary>
    internal static IEnumerable<Plan> DefaultPlans()
    {
        var home = new Plan("HOME", "Maisons & Villas", AccountKind.Household, Money.FromFcfa(10_000), Money.FromFcfa(8_500), 1, diagnosticRequired: false);
        home.SetServiceLevel(priority: 1, includedVisitsPerPeriod: null);
        yield return home;

        // Default service levels (data, edited by admins): higher offers answer faster and visit more.
        var business = new (string Code, string Name, long Monthly, int Priority, int Visits, int CriticalHours, int HighHours, int NormalHours)[]
        {
            ("ESSENTIEL", "Essentiel", 1_500_000, 2, 1, 4, 24, 72),
            ("PROFESSIONNEL", "Professionnel", 2_500_000, 3, 2, 4, 12, 48),
            ("PREMIUM", "Premium", 3_500_000, 4, 3, 2, 8, 24),
        };
        foreach (var (code, name, monthly, priority, visits, critical, high, normal) in business)
        {
            var plan = new Plan(code, name, AccountKind.Business, Money.FromFcfa(75_000), Money.FromFcfa(monthly), 3, diagnosticRequired: true);
            plan.SetServiceLevel(priority, includedVisitsPerPeriod: visits);
            plan.SetSla(Urgency.Critical, TimeSpan.FromHours(critical));
            plan.SetSla(Urgency.High, TimeSpan.FromHours(high));
            plan.SetSla(Urgency.Normal, TimeSpan.FromHours(normal));
            yield return plan;
        }
    }

    private async Task SeedLocationsAsync(CancellationToken cancellationToken)
    {
        var data = await LoadLocationsAsync(cancellationToken);
        var regions = await db.Regions.ToDictionaryAsync(r => r.Slug, cancellationToken);
        var departments = await db.Departments.ToDictionaryAsync(d => (d.RegionId, d.Slug), cancellationToken);
        var communes = await db.Communes.ToDictionaryAsync(c => (c.DepartmentId, c.Slug), cancellationToken);
        var neighborhoods = (await db.Neighborhoods.Select(n => new { n.CommuneId, n.Slug }).ToListAsync(cancellationToken))
            .Select(n => (n.CommuneId, n.Slug))
            .ToHashSet();
        var added = 0;

        foreach (var regionData in data.Regions)
        {
            var regionSlug = Slug.From(regionData.Name);
            if (!regions.TryGetValue(regionSlug, out var region))
            {
                region = new Region(Guid.CreateVersion7(), regionData.Name, regionSlug);
                db.Regions.Add(region);
                regions[regionSlug] = region;
                added++;
            }

            foreach (var departmentData in regionData.Departments)
            {
                var departmentSlug = Slug.From(departmentData.Name);
                if (!departments.TryGetValue((region.Id, departmentSlug), out var department))
                {
                    department = new Department(Guid.CreateVersion7(), region.Id, departmentData.Name, departmentSlug);
                    db.Departments.Add(department);
                    departments[(region.Id, departmentSlug)] = department;
                    added++;
                }

                foreach (var communeData in departmentData.Communes)
                {
                    var communeSlug = Slug.From(communeData.Name);
                    if (!communes.TryGetValue((department.Id, communeSlug), out var commune))
                    {
                        commune = new Commune(Guid.CreateVersion7(), department.Id, communeData.Name, communeSlug);
                        db.Communes.Add(commune);
                        communes[(department.Id, communeSlug)] = commune;
                        added++;
                    }

                    var neighborhoodNames = communeData.Neighborhoods is { Count: > 0 } names ? names : [communeData.Name];
                    foreach (var name in neighborhoodNames)
                    {
                        var slug = Slug.From(name);
                        if (neighborhoods.Add((commune.Id, slug)))
                        {
                            db.Neighborhoods.Add(new Neighborhood(Guid.CreateVersion7(), commune.Id, name, slug));
                            added++;
                        }
                    }
                }
            }
        }

        if (added > 0)
        {
            LogSeeded(logger, added, "locations");
        }
    }

    private async Task SeedPlansAsync(CancellationToken cancellationToken)
    {
        // Repair: early seeds shared the default SLA instances between plans, so some lost them.
        foreach (var plan in await db.Plans.ToListAsync(cancellationToken))
        {
            plan.EnsureDefaultSla();
        }

        var existing = await db.Plans.Select(p => p.Code).ToListAsync(cancellationToken);
        var missing = DefaultPlans().Where(p => !existing.Contains(p.Code)).ToList();
        db.Plans.AddRange(missing);
        if (missing.Count > 0)
        {
            LogSeeded(logger, missing.Count, "plans");
        }
    }

    private async Task SeedSettingsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.AppSettings.Select(s => s.Key).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var (key, value) in SettingKeys.Defaults.Where(d => !existing.Contains(d.Key)))
        {
            db.AppSettings.Add(new AppSetting(key, value, now));
        }
    }

    /// <summary>Every neighborhood gets a program (0 members) so the « 0 / 500 voisins » gauge works everywhere.</summary>
    private async Task SeedNeighborhoodProgramsAsync(CancellationToken cancellationToken)
    {
        var thresholdSetting = await db.AppSettings
            .Where(s => s.Key == SettingKeys.NeighborhoodThreshold)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var threshold = int.TryParse(thresholdSetting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
            ? t
            : NeighborhoodProgram.DefaultThreshold;

        var withoutProgram = await db.Neighborhoods
            .Where(n => !db.NeighborhoodPrograms.Any(p => p.NeighborhoodId == n.Id))
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        db.NeighborhoodPrograms.AddRange(withoutProgram.Select(id => new NeighborhoodProgram(id, threshold)));
    }

    private static async Task<LocationsFile> LoadLocationsAsync(CancellationToken cancellationToken)
    {
        await using var stream = typeof(DatabaseSeeder).Assembly.GetManifestResourceStream(LocationsResource)
            ?? throw new InvalidOperationException($"Embedded resource {LocationsResource} not found.");
        return await JsonSerializer.DeserializeAsync<LocationsFile>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Invalid locations file.");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} {Kind}")]
    private static partial void LogSeeded(ILogger logger, int count, string kind);

    private sealed record LocationsFile(IReadOnlyList<RegionData> Regions);

    private sealed record RegionData(string Name, IReadOnlyList<DepartmentData> Departments);

    private sealed record DepartmentData(string Name, IReadOnlyList<CommuneData> Communes);

    private sealed record CommuneData(string Name, IReadOnlyList<string>? Neighborhoods);
}
