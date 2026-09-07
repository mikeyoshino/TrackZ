using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using TrackZ.Mobile.Features.Auth;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.History;
using TrackZ.Mobile.Features.Summary;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Localization;

public sealed class LocalizedUiScope : IDisposable
{
    private IServiceScope? _scope;

    internal LocalizedUiScope(IServiceScope scope) => _scope = scope;

    public IServiceProvider Services =>
        _scope?.ServiceProvider
        ?? throw new ObjectDisposedException(nameof(LocalizedUiScope));

    public void Dispose() => Interlocked.Exchange(ref _scope, null)?.Dispose();
}

public sealed class LocalizedUiScopeManager
{
    private static readonly object RouteGate = new();
    private readonly IServiceScopeFactory _scopes;
    private readonly object _gate = new();
    private LocalizedUiScope? _active;

    public LocalizedUiScopeManager(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
        RegisterRoutes();
    }

    public IServiceProvider ActiveServices
    {
        get
        {
            lock (_gate)
                return (_active ?? throw new InvalidOperationException(
                    "The localized UI scope is not active.")).Services;
        }
    }

    public LocalizedUiScope CreateCandidate() => new(_scopes.CreateScope());

    public LocalizedUiScope? Activate(LocalizedUiScope candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            var previous = _active;
            _active = candidate;
            return previous;
        }
    }

    public LocalizedUiScope? Deactivate()
    {
        lock (_gate)
        {
            var previous = _active;
            _active = null;
            return previous;
        }
    }

    private void RegisterRoutes()
    {
        lock (RouteGate)
        {
            Register(nameof(ExercisePickerPage), new ScopedRouteFactory<ExercisePickerPage>(this));
            Register(nameof(ExerciseTechniquePage), new ScopedRouteFactory<ExerciseTechniquePage>(this));
            Register("active-workout", new ScopedRouteFactory<WorkoutPage>(this));
            Register(nameof(CustomExercisePage), new ScopedRouteFactory<CustomExercisePage>(this));
            Register(nameof(SetLoggerPage), new ScopedRouteFactory<SetLoggerPage>(this));
            Register(nameof(WorkoutHistoryPage), new ScopedRouteFactory<WorkoutHistoryPage>(this));
            Register("workout-history-detail", new ScopedRouteFactory<WorkoutHistoryDetailPage>(this));
            Register(nameof(WorkoutSummaryPage), new ScopedRouteFactory<WorkoutSummaryPage>(this));
            Register("create-account", new ScopedRouteFactory<CreateAccountPage>(this));
        }
    }

    private static void Register(string route, RouteFactory factory)
    {
        Routing.UnRegisterRoute(route);
        Routing.RegisterRoute(route, factory);
    }
}

public sealed class LocalizedUiInstallation(LocalizedUiScope scope, Page root) : IDisposable
{
    public LocalizedUiScope Scope { get; } = scope;
    public Page Root { get; } = root;
    public void Dispose() => Scope.Dispose();
}

public interface ILocalizedUiHost
{
    string? CurrentRootTabRoute { get; }
    LocalizedUiInstallation Prepare(AuthGateSnapshot snapshot);
    Task InstallAsync(
        LocalizedUiInstallation installation,
        string? rootTabRoute,
        CancellationToken cancellationToken);
}

internal sealed class ScopedRouteFactory<TPage>(LocalizedUiScopeManager scopes) : RouteFactory
    where TPage : Element
{
    public override Element GetOrCreate() =>
        scopes.ActiveServices.GetRequiredService<TPage>();

    public override Element GetOrCreate(IServiceProvider services) => GetOrCreate();
}
