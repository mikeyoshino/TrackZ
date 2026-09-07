using Microsoft.EntityFrameworkCore;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Api;

public sealed record ExerciseCatalogDeploymentCommand(string ManifestPath)
{
    public static ExerciseCatalogDeploymentCommand? Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "deploy-exercise-catalog", StringComparison.Ordinal))
            return null;
        if (args is not ["deploy-exercise-catalog", "--manifest", var manifest]
            || string.IsNullOrWhiteSpace(manifest))
            throw new ArgumentException(
                "Usage: TrackZ.Api deploy-exercise-catalog --manifest <path-to-catalog.json>",
                nameof(args));
        return new ExerciseCatalogDeploymentCommand(Path.GetFullPath(manifest));
    }

    public async Task ExecuteAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ExerciseCatalogDeploymentService>()
            .DeployAndSeedAsync(ManifestPath, cancellationToken);

        var environment = services.GetRequiredService<IHostEnvironment>();
        if (environment.IsProduction()
            && string.Equals(environment.EnvironmentName, Environments.Production, StringComparison.Ordinal))
        {
            await ProductionExerciseCatalogPublicationCommand
                .ForApprovedRepositoryCatalog(ManifestPath)
                .ExecuteAsync(services, cancellationToken);
        }
    }
}
