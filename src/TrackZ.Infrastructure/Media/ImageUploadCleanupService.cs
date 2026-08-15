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
            catch (Exception exception) { logger.LogWarning(exception, "Exercise image upload cleanup scan failed."); }
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
            try
            {
                if (candidate.StagingKey is not null)
                    await storage.DeleteAsync($"staging/{candidate.OwnerId:D}/", candidate.StagingKey, CancellationToken.None);
                if (candidate.ProcessingLeaseId is { } lease)
                {
                    var root = $"private/{candidate.OwnerId:D}/{candidate.ExerciseId:D}/{candidate.TicketId:D}/{lease:D}";
                    await storage.DeleteAsync($"private/{candidate.OwnerId:D}/", root + "/master.jpg", CancellationToken.None);
                    await storage.DeleteAsync($"private/{candidate.OwnerId:D}/", root + "/thumbnail.jpg", CancellationToken.None);
                }
                await store.MarkCleanupCompleteAsync(candidate.TicketId, candidate.StagingKey, candidate.ProcessingLeaseId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                // Do not log object keys or owner identifiers. The durable candidate remains for the next run.
                logger.LogWarning(exception, "Exercise image upload cleanup attempt failed.");
            }
        }
    }
}
