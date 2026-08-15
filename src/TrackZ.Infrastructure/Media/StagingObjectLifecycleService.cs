using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TrackZ.Infrastructure.Media;

public sealed class StagingObjectLifecycleService(
    IStagingObjectLifecycle lifecycle,
    ILogger<StagingObjectLifecycleService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await lifecycle.EnsureConfiguredAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogCritical(
                "Staging object lifecycle startup failed. FailureType: {FailureType}",
                exception.GetType().FullName ?? exception.GetType().Name);
            throw new InvalidOperationException("Staging object lifecycle configuration failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
