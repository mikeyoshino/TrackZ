using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using TrackZ.Api;

namespace TrackZ.Api.Tests.Exercises;

public sealed class ProductionExerciseCatalogPublicationCommandTests
{
    private const string ManifestSha256 = "f59f571c0655d82a05316dab3d3bda89008c77745e43d6ac733e4580b46c2f1d";

    [Fact]
    public void Production_publication_command_requires_an_exact_approval_contract()
    {
        var parsed = ProductionExerciseCatalogPublicationCommand.Parse(
        [
            "publish-approved-exercise-catalog",
            "--manifest", "assets/exercises/catalog.json",
            "--reviewer-email", " mikeyoshinos@gmail.com ",
            "--rights-reference", " project-owner-approved-2026-09-07 ",
            "--approved-manifest-sha256", ManifestSha256
        ]);

        Assert.NotNull(parsed);
        Assert.Equal(Path.GetFullPath("assets/exercises/catalog.json"), parsed.ManifestPath);
        Assert.Equal("MIKEYOSHINOS@GMAIL.COM", parsed.NormalizedReviewerEmail);
        Assert.Equal("project-owner-approved-2026-09-07", parsed.RightsReference);
        Assert.Equal(ManifestSha256, parsed.ApprovedManifestSha256);
        Assert.Null(ProductionExerciseCatalogPublicationCommand.Parse([]));
        Assert.Null(ProductionExerciseCatalogPublicationCommand.Parse(["--urls", "http://localhost:5000"]));
    }

    [Fact]
    public void Repository_approval_factory_is_pinned_to_the_owner_approved_catalog()
    {
        var command = ProductionExerciseCatalogPublicationCommand.ForApprovedRepositoryCatalog(
            "assets/exercises/catalog.json");

        Assert.Equal("MIKEYOSHINOS@GMAIL.COM", command.NormalizedReviewerEmail);
        Assert.Equal("project-owner-approved-2026-09-07", command.RightsReference);
        Assert.Equal(ManifestSha256, command.ApprovedManifestSha256);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("production")]
    [InlineData("Production ")]
    [InlineData("")]
    public async Task Production_publication_command_rejects_every_non_exact_production_environment(string environmentName)
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName))
            .BuildServiceProvider();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => RequiredCommand().ExecuteAsync(services));

        Assert.Equal("Approved exercise catalog publication is allowed only in the exact Production environment.", error.Message);
    }

    [Fact]
    public async Task Production_publication_command_rejects_a_manifest_not_covered_by_the_approval()
    {
        var path = Path.GetTempFileName();
        await File.WriteAllTextAsync(path, "changed after approval");
        try
        {
            var command = ProductionExerciseCatalogPublicationCommand.Parse(
            [
                "publish-approved-exercise-catalog",
                "--manifest", path,
                "--reviewer-email", "mikeyoshinos@gmail.com",
                "--rights-reference", "project-owner-approved-2026-09-07",
                "--approved-manifest-sha256", ManifestSha256
            ])!;
            var services = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new TestHostEnvironment("Production"))
                .BuildServiceProvider();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => command.ExecuteAsync(services));

            Assert.Equal("The exercise catalog manifest does not match the approved SHA-256 digest.", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ProductionExerciseCatalogPublicationCommand RequiredCommand() =>
        ProductionExerciseCatalogPublicationCommand.Parse(
        [
            "publish-approved-exercise-catalog",
            "--manifest", "assets/exercises/catalog.json",
            "--reviewer-email", "mikeyoshinos@gmail.com",
            "--rights-reference", "project-owner-approved-2026-09-07",
            "--approved-manifest-sha256", ManifestSha256
        ])!;

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "TrackZ.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
