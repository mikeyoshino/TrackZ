using Microsoft.Extensions.DependencyInjection.Extensions;
using TrackZ.Infrastructure.Media;

namespace Microsoft.Extensions.DependencyInjection;

internal static class TrackZApiTestServiceCollectionExtensions
{
    public static IServiceCollection ReplaceStagingLifecycleWithNoOpForTests(this IServiceCollection services)
    {
        services.RemoveAll<IStagingObjectLifecycle>();
        services.AddSingleton<IStagingObjectLifecycle, NoOpStagingObjectLifecycle>();
        return services;
    }

    private sealed class NoOpStagingObjectLifecycle : IStagingObjectLifecycle
    {
        public Task EnsureConfiguredAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
