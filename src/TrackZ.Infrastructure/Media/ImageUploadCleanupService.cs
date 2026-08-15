using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrackZ.Application.Common.Interfaces;
using TrackZ.Application.Media;

namespace TrackZ.Infrastructure.Media;

public sealed class ImageUploadCleanupOptions
{
    public const string SectionName = "ImageUploadCleanup";
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>Idempotently removes lease-scoped abandoned objects; completed image keys are never candidates.</summary>
public sealed class ImageUploadCleanupService(IServiceScopeFactory scopes, IOptions<ImageUploadCleanupOptions> options, ILogger<ImageUploadCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval < TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : options.Value.Interval);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogWarning("Exercise image upload cleanup scan failed ({ExceptionType}).", exception.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExerciseImageUploadStore>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var candidates = await store.ListCleanupCandidatesAsync(DateTimeOffset.UtcNow, cancellationToken);
        foreach (var candidate in candidates)
        {
            ImageUploadCleanupCandidate? claim = null;
            try
            {
                claim = await store.TryClaimCleanupAsync(candidate, DateTimeOffset.UtcNow, cancellationToken);
                if (claim is null) continue;
                if (claim.StagingKey is not null)
                    await storage.DeleteAsync($"staging/{claim.OwnerId:D}/", claim.StagingKey, CancellationToken.None);
                if (claim.ProcessingLeaseId is { } lease)
                {
                    var root = $"private/{claim.OwnerId:D}/{claim.ExerciseId:D}/{claim.TicketId:D}/{lease:D}";
                    await storage.DeleteAsync($"private/{claim.OwnerId:D}/", root + "/master.jpg", CancellationToken.None);
                    await storage.DeleteAsync($"private/{claim.OwnerId:D}/", root + "/thumbnail.jpg", CancellationToken.None);
                }
                await store.CompleteCleanupClaimAsync(claim.TicketId, claim.CleanupClaimId!.Value, CancellationToken.None);
            }
            catch (Exception exception)
            {
                if (claim?.CleanupClaimId is { } claimId)
                    await store.ReleaseCleanupClaimAsync(claim.TicketId, claimId, CancellationToken.None);
                // Do not log object keys, owner identifiers, or SDK exception text.
                logger.LogWarning("Exercise image upload cleanup attempt failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }
}
