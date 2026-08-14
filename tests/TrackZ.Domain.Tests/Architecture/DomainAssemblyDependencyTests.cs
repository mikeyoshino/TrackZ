namespace TrackZ.Domain.Tests.Architecture;

public sealed class DomainAssemblyDependencyTests
{
    [Fact]
    public void Domain_assembly_does_not_depend_on_higher_layers()
    {
        var dependencyNames = typeof(TrackZ.Domain.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name);

        Assert.DoesNotContain("TrackZ.Application", dependencyNames);
        Assert.DoesNotContain("TrackZ.Infrastructure", dependencyNames);
        Assert.DoesNotContain("TrackZ.Api", dependencyNames);
    }
}
