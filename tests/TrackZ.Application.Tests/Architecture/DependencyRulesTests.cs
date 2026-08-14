using NetArchTest.Rules;

namespace TrackZ.Application.Tests.Architecture;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_Must_Not_Depend_On_Outer_Layers()
    {
        var result = Types.InAssembly(typeof(TrackZ.Domain.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOnAny("TrackZ.Application", "TrackZ.Infrastructure", "TrackZ.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_Must_Not_Depend_On_Infrastructure_Or_Api()
    {
        var result = Types.InAssembly(typeof(TrackZ.Application.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOnAny("TrackZ.Infrastructure", "TrackZ.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }
}
