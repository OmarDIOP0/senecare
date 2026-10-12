namespace SeneCare.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_does_not_depend_on_other_SeneCare_projects()
    {
        var references = typeof(AssemblyMarker).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, r => r.Name!.StartsWith("SeneCare.", StringComparison.Ordinal));
    }
}
