using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Api;

public sealed record ExerciseCatalogPublicationCommand(
    string ManifestPath,
    Guid ReviewerId,
    string RightsReference)
{
    public static ExerciseCatalogPublicationCommand? Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "publish-exercise-catalog", StringComparison.Ordinal))
            return null;

        if (args is not
            [
                "publish-exercise-catalog",
                "--manifest", var manifest,
                "--reviewer-id", var reviewer,
                "--rights-reference", var rightsReference
            ]
            || string.IsNullOrWhiteSpace(manifest)
            || !Guid.TryParseExact(reviewer, "D", out var reviewerId)
            || reviewerId == Guid.Empty)
        {
            throw UsageError();
        }

        var normalizedRightsReference = rightsReference?.Trim() ?? string.Empty;
        if (normalizedRightsReference.Length is 0 or > 512)
            throw UsageError();

        return new ExerciseCatalogPublicationCommand(
            Path.GetFullPath(manifest),
            reviewerId,
            normalizedRightsReference);
    }

    public async Task ExecuteAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment()
            || !string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Exercise catalog publication is allowed only in the exact Development environment.");
        }

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ExerciseCatalogPublicationService>()
            .PublishAsync(ManifestPath, ReviewerId, RightsReference, cancellationToken);
    }

    private static ArgumentException UsageError() => new(
        "Usage: TrackZ.Api publish-exercise-catalog --manifest <path-to-catalog.json> --reviewer-id <guid> --rights-reference <reference>",
        "args");
}
