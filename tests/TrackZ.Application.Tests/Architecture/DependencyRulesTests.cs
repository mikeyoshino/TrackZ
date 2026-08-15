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

    [Fact]
    public void Application_Must_Not_Depend_On_Entity_Framework_Core()
    {
        var result = Types.InAssembly(typeof(TrackZ.Application.AssemblyMarker).Assembly)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_Must_Not_Reference_EfCore_Or_Npgsql_Assemblies()
    {
        var references = typeof(TrackZ.Application.AssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Npgsql", references);
        Assert.DoesNotContain("Npgsql.EntityFrameworkCore.PostgreSQL", references);
    }

    [Fact]
    public void AppDbContext_Port_Must_Not_Expose_Entity_Framework_Core_Types()
    {
        var exposedEfType = typeof(TrackZ.Application.Common.Interfaces.IAppDbContext)
            .GetMembers()
            .SelectMany(member => member switch
            {
                System.Reflection.PropertyInfo property => [property.PropertyType],
                System.Reflection.MethodInfo method => method.GetParameters()
                    .Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType),
                _ => [],
            })
            .FirstOrDefault(IsEntityFrameworkCoreType);

        Assert.Null(exposedEfType);
    }

    private static bool IsEntityFrameworkCoreType(Type type) =>
        type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
        || type.GetGenericArguments().Any(IsEntityFrameworkCoreType);
}
