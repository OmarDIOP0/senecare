using System.Text.Json;
using SeneCare.Domain.Common;
using SeneCare.Infrastructure.Persistence;

namespace SeneCare.Infrastructure.IntegrationTests;

/// <summary>Checks the embedded reference data without needing a database.</summary>
public class SeedDataTests
{
    [Fact]
    public void Locations_file_lists_14_regions_and_46_departments_with_unique_slugs()
    {
        using var stream = typeof(SeneCareDbContext).Assembly
            .GetManifestResourceStream("SeneCare.Infrastructure.Persistence.Seed.senegal-locations.json")!;
        using var json = JsonDocument.Parse(stream);
        var regions = json.RootElement.GetProperty("regions").EnumerateArray().ToList();
        var departments = regions.SelectMany(r => r.GetProperty("departments").EnumerateArray()).ToList();

        Assert.Equal(14, regions.Count);
        Assert.Equal(46, departments.Count);
        Assert.Equal(14, regions.Select(r => Slug.From(r.GetProperty("name").GetString()!)).Distinct().Count());
        Assert.All(departments, d =>
        {
            var communes = d.GetProperty("communes").EnumerateArray().Select(c => Slug.From(c.GetProperty("name").GetString()!)).ToList();
            Assert.NotEmpty(communes);
            Assert.Equal(communes.Count, communes.Distinct().Count());
        });
    }
}
