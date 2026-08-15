using TrackZ.Api;

namespace TrackZ.Api.Tests.Exercises;

public sealed class ExerciseCatalogDeploymentCommandTests
{
    [Fact]
    public void Exact_deployment_command_requires_an_explicit_manifest()
    {
        Assert.Null(ExerciseCatalogDeploymentCommand.Parse([]));
        Assert.Null(ExerciseCatalogDeploymentCommand.Parse(["--urls", "http://localhost:5000"]));

        var parsed = ExerciseCatalogDeploymentCommand.Parse(
            ["deploy-exercise-catalog", "--manifest", "assets/exercises/catalog.json"]);

        Assert.NotNull(parsed);
        Assert.Equal(
            Path.GetFullPath("assets/exercises/catalog.json"),
            parsed.ManifestPath);
        Assert.Throws<ArgumentException>(() =>
            ExerciseCatalogDeploymentCommand.Parse(["deploy-exercise-catalog"]));
        Assert.Throws<ArgumentException>(() =>
            ExerciseCatalogDeploymentCommand.Parse(
                ["deploy-exercise-catalog", "--manifest", "catalog.json", "unexpected"]));
    }
}
