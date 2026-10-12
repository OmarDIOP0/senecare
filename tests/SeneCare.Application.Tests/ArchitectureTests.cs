namespace SeneCare.Application.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Application_depends_only_on_Domain()
    {
        var seneCareReferences = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies()
            .Select(r => r.Name)
            .Where(n => n!.StartsWith("SeneCare.", StringComparison.Ordinal))
            .ToList();

        Assert.All(seneCareReferences, n => Assert.Equal("SeneCare.Domain", n));
    }
}
