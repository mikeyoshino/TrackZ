using Microsoft.Extensions.Hosting;

namespace TrackZ.Infrastructure.Media;

public sealed class StagingObjectLifecycleService(IStagingObjectLifecycle lifecycle) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        lifecycle.EnsureConfiguredAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
