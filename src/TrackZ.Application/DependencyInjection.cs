using Microsoft.Extensions.DependencyInjection;

namespace TrackZ.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddLogging();
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
            configuration.AddOpenBehavior(typeof(Workouts.WorkoutRuleExceptionBehavior<,>));
        });

        return services;
    }
}
