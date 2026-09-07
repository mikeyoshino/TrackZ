using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using TrackZ.Infrastructure.Persistence;
using TrackZ.Infrastructure.Persistence.Seed;

namespace TrackZ.Api;

public sealed record ProductionExerciseCatalogPublicationCommand(
    string ManifestPath,
    string NormalizedReviewerEmail,
    string RightsReference,
    string ApprovedManifestSha256)
{
    private const string ApprovedReviewerEmail = "MIKEYOSHINOS@GMAIL.COM";
    private const string ApprovedRightsReference = "project-owner-approved-2026-09-07";
    private const string ApprovedRepositoryManifestSha256 = "f59f571c0655d82a05316dab3d3bda89008c77745e43d6ac733e4580b46c2f1d";

    public static ProductionExerciseCatalogPublicationCommand ForApprovedRepositoryCatalog(string manifestPath) => new(
        Path.GetFullPath(manifestPath),
        ApprovedReviewerEmail,
        ApprovedRightsReference,
        ApprovedRepositoryManifestSha256);

    public static ProductionExerciseCatalogPublicationCommand? Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "publish-approved-exercise-catalog", StringComparison.Ordinal))
            return null;

        if (args is not
            [
                "publish-approved-exercise-catalog",
                "--manifest", var manifest,
                "--reviewer-email", var reviewerEmail,
                "--rights-reference", var rightsReference,
                "--approved-manifest-sha256", var approvedManifestSha256
            ])
            throw UsageError();

        var normalizedEmail = reviewerEmail?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedRightsReference = rightsReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(manifest)
            || normalizedEmail.Length is 0 or > 320
            || !normalizedEmail.Contains('@', StringComparison.Ordinal)
            || normalizedRightsReference.Length is 0 or > 512
            || approvedManifestSha256 is null
            || approvedManifestSha256.Length != 64
            || approvedManifestSha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw UsageError();

        return new ProductionExerciseCatalogPublicationCommand(
            Path.GetFullPath(manifest),
            normalizedEmail,
            normalizedRightsReference,
            approvedManifestSha256);
    }

    public async Task ExecuteAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsProduction()
            || !string.Equals(environment.EnvironmentName, Environments.Production, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Approved exercise catalog publication is allowed only in the exact Production environment.");

        await VerifyApprovedManifestAsync(cancellationToken);

        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync(cancellationToken);
        var reviewer = await database.Users.AsNoTracking().SingleOrDefaultAsync(
            user => user.NormalizedEmail == NormalizedReviewerEmail,
            cancellationToken);
        if (reviewer is null)
            throw new InvalidOperationException("The approved exercise catalog reviewer account was not found.");

        await scope.ServiceProvider.GetRequiredService<ExerciseCatalogPublicationService>()
            .PublishAsync(ManifestPath, reviewer.Id, RightsReference, cancellationToken);
    }

    private async Task VerifyApprovedManifestAsync(CancellationToken cancellationToken)
    {
        await using var source = File.OpenRead(ManifestPath);
        var digest = await SHA256.HashDataAsync(source, cancellationToken);
        var actual = Convert.ToHexStringLower(digest);
        if (!string.Equals(actual, ApprovedManifestSha256, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The exercise catalog manifest does not match the approved SHA-256 digest.");
    }

    private static ArgumentException UsageError() => new(
        "Usage: TrackZ.Api publish-approved-exercise-catalog --manifest <path-to-catalog.json> --reviewer-email <email> --rights-reference <reference> --approved-manifest-sha256 <64 lowercase hex characters>",
        "args");
}
