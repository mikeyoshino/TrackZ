using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TrackZ.Api;

namespace TrackZ.Api.Tests.Exercises;

public sealed class ExerciseCatalogPublicationCommandTests
{
    private static readonly Guid ReviewerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Publication_command_requires_exact_contract()
    {
        var parsed = ExerciseCatalogPublicationCommand.Parse(
        [
            "publish-exercise-catalog",
            "--manifest", "assets/exercises/catalog.json",
            "--reviewer-id", ReviewerId.ToString("D"),
            "--rights-reference", "local-simulator-review-2026-08-21"
        ]);

        Assert.NotNull(parsed);
        Assert.Equal(Path.GetFullPath("assets/exercises/catalog.json"), parsed.ManifestPath);
        Assert.Equal(ReviewerId, parsed.ReviewerId);
        Assert.Equal("local-simulator-review-2026-08-21", parsed.RightsReference);

        var maximumRights = new string('r', 512);
        var boundary = ExerciseCatalogPublicationCommand.Parse(
        [
            "publish-exercise-catalog",
            "--manifest", "assets/exercises/catalog.json",
            "--reviewer-id", ReviewerId.ToString("D"),
            "--rights-reference", $" {maximumRights} "
        ]);
        Assert.Equal(maximumRights, boundary!.RightsReference);

        Assert.Null(ExerciseCatalogPublicationCommand.Parse([]));
        Assert.Null(ExerciseCatalogPublicationCommand.Parse(["--urls", "http://localhost:5000"]));
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void Publication_command_rejects_hostile_argument_shapes(string[] args)
    {
        Assert.Throws<ArgumentException>(() => ExerciseCatalogPublicationCommand.Parse(args));
    }

    [Fact]
    public async Task Publication_command_fails_before_database_access_outside_development()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment("Production"))
            .BuildServiceProvider();
        var command = RequiredCommand();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(services));

        Assert.Contains("Development", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    [InlineData("Development ")]
    [InlineData("Staging")]
    [InlineData("")]
    public async Task Publication_command_rejects_every_non_exact_development_environment(string environmentName)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName))
            .BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RequiredCommand().ExecuteAsync(services));

        Assert.Equal(
            "Exercise catalog publication is allowed only in the exact Development environment.",
            error.Message);
    }

    public static TheoryData<string[]> InvalidArguments => new()
    {
        new[] { "publish-exercise-catalog" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D") },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference" },
        new[] { "publish-exercise-catalog", "--reviewer-id", ReviewerId.ToString("D"), "--manifest", "catalog.json", "--rights-reference", "rights" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--rights-reference", "rights", "--reviewer-id", ReviewerId.ToString("D") },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--manifest", "other.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", "rights" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", "rights", "extra" },
        new[] { "publish-exercise-catalog", "--manifest", "", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", "rights" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", "not-a-guid", "--rights-reference", "rights" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", Guid.Empty.ToString("D"), "--rights-reference", "rights" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", "" },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", null! },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", "   " },
        new[] { "publish-exercise-catalog", "--manifest", "catalog.json", "--reviewer-id", ReviewerId.ToString("D"), "--rights-reference", new string('x', 513) }
    };

    private static ExerciseCatalogPublicationCommand RequiredCommand() =>
        ExerciseCatalogPublicationCommand.Parse(
        [
            "publish-exercise-catalog",
            "--manifest", "assets/exercises/catalog.json",
            "--reviewer-id", ReviewerId.ToString("D"),
            "--rights-reference", "local-simulator-review-2026-08-21"
        ])!;

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TrackZ.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
