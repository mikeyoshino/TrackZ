using TrackZ.Domain.Exercises;
using TrackZ.Mobile.Data;
using TrackZ.Mobile.Data.Models;
using TrackZ.Mobile.Features.Exercises;
using TrackZ.Mobile.Features.Exercises.Data;
using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Features.Profile;
using TrackZ.Mobile.Features.Workout;
using TrackZ.Mobile.Identity;
using TrackZ.Mobile.Sync;

namespace TrackZ.Mobile.Tests.Profile;

public sealed class ProfileSignOutTests : IDisposable
{
    private readonly string _workoutDatabasePath = Path.Combine(
        Path.GetTempPath(), $"trackz-profile-signout-workouts-{Guid.NewGuid():N}.db");
    private readonly string _exerciseDatabasePath = Path.Combine(
        Path.GetTempPath(), $"trackz-profile-signout-exercises-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Clear_local_state_requires_confirmation_before_signing_out()
    {
        var auth = Authentication();
        var controller = new ProfileSignOutController(
            new FixedRiskSource(ProfileSignOutRisk.Clear),
            new RiskAwareConfirmation(confirmClear: true, confirmAtRisk: false),
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.Equal(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(1, auth.Identity.LogoutCalls);
        Assert.Equal(1, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Account_write_cannot_commit_after_the_final_clear_risk_check()
    {
        var auth = Authentication();
        auth.Identity.GateLogout();
        var generation = auth.Boundary.Capture();
        var controller = new ProfileSignOutController(
            new FixedRiskSource(ProfileSignOutRisk.Clear),
            new RiskAwareConfirmation(confirmClear: true, confirmAtRisk: false),
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        var signOut = controller.SignOutCommand.ExecuteAsync();
        await auth.Identity.LogoutEntered;
        var mutationRan = false;
        var mutation = auth.Boundary.TryCommitAsync(generation, _ =>
        {
            mutationRan = true;
            return Task.CompletedTask;
        });
        Assert.False(mutation.IsCompleted);

        auth.Identity.ReleaseLogout();
        await signOut;

        Assert.False(await mutation);
        Assert.False(mutationRan);
        Assert.Equal(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
    }

    [Fact]
    public async Task New_risk_after_the_clear_confirmation_requires_the_destructive_confirmation()
    {
        var auth = Authentication();
        var confirmation = new RiskAwareConfirmation(
            confirmClear: true, confirmAtRisk: false);
        var controller = new ProfileSignOutController(
            new SequenceRiskSource(
                ProfileSignOutRisk.Clear,
                new ProfileSignOutRisk(1, 0, false)),
            confirmation,
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.Equal([false, true], confirmation.DestructivePrompts);
        Assert.NotEqual(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(0, auth.Identity.LogoutCalls);
        Assert.Equal(0, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Declining_data_loss_confirmation_preserves_the_signed_in_session()
    {
        var auth = Authentication();
        var controller = new ProfileSignOutController(
            new FixedRiskSource(new ProfileSignOutRisk(2, 1, true)),
            new RiskAwareConfirmation(confirmClear: true, confirmAtRisk: false),
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.NotEqual(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(0, auth.Identity.LogoutCalls);
        Assert.Equal(0, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Explicit_data_loss_confirmation_allows_local_sign_out()
    {
        var auth = Authentication();
        var controller = new ProfileSignOutController(
            new FixedRiskSource(new ProfileSignOutRisk(2, 1, true)),
            new RiskAwareConfirmation(confirmClear: false, confirmAtRisk: true),
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.Equal(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(1, auth.Identity.LogoutCalls);
        Assert.Equal(1, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Final_unknown_risk_requires_a_stronger_warning_after_known_data_loss_was_confirmed()
    {
        var auth = Authentication();
        var confirmation = new SequenceConfirmation(true, false);
        var controller = new ProfileSignOutController(
            new SequenceRiskSource(
                new ProfileSignOutRisk(1, 0, false),
                ProfileSignOutRisk.Unknown),
            confirmation,
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.Equal([1, 2], confirmation.WarningStrengths);
        Assert.NotEqual(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(0, auth.Identity.LogoutCalls);
        Assert.Equal(0, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Initial_unknown_confirmation_covers_the_final_risk_without_a_duplicate_prompt()
    {
        var auth = Authentication();
        var confirmation = new SequenceConfirmation(true);
        var controller = new ProfileSignOutController(
            new SequenceRiskSource(
                ProfileSignOutRisk.Unknown,
                new ProfileSignOutRisk(2, 1, true)),
            confirmation,
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.Equal([2], confirmation.WarningStrengths);
        Assert.Equal(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(1, auth.Identity.LogoutCalls);
        Assert.Equal(1, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Unknown_risk_decline_keeps_the_session_after_the_strongest_warning()
    {
        var auth = Authentication();
        var text = MobileResources.ForCulture(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var controller = new ProfileSignOutController(
            new FailingRiskSource(),
            new RiskAwareConfirmation(confirmClear: true, confirmAtRisk: false),
            auth.Coordinator,
            text);

        await controller.SignOutCommand.ExecuteAsync();

        Assert.NotEqual(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Null(controller.ErrorMessage);
        Assert.Equal(0, auth.Identity.LogoutCalls);
        Assert.Equal(0, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Unknown_risk_explicit_confirmation_still_allows_local_sign_out()
    {
        var auth = Authentication();
        var confirmation = new RiskAwareConfirmation(
            confirmClear: false, confirmAtRisk: true);
        var controller = new ProfileSignOutController(
            new FailingRiskSource(),
            confirmation,
            auth.Coordinator,
            MobileResources.ForCulture(System.Globalization.CultureInfo.GetCultureInfo("en-US")));

        await controller.SignOutCommand.ExecuteAsync();

        Assert.Equal([true], confirmation.DestructivePrompts);
        Assert.Equal(AuthGateState.SignedOut, auth.Coordinator.Snapshot.State);
        Assert.Equal(1, auth.Identity.LogoutCalls);
        Assert.Equal(1, auth.Cleaner.ClearCount);
    }

    [Fact]
    public async Task Risk_source_detects_active_workout_and_all_durable_unsynced_changes()
    {
        var workoutDatabase = new TrackZLocalDatabase(_workoutDatabasePath);
        var workouts = new LocalWorkoutRepository(workoutDatabase);
        var boundary = new AccountSessionBoundary();
        var active = new ActiveWorkoutCoordinator(workouts, boundary, new FixedClock());
        var outbox = new OutboxRepository(workoutDatabase);
        var exercises = new ExerciseCache(_exerciseDatabasePath);
        var source = new ProfileSignOutRiskSource(outbox, exercises, workouts);

        Assert.Equal(ProfileSignOutRisk.Clear, await source.GetRiskAsync());

        await active.StartAsync([
            new WorkoutExerciseSelection(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TrackingMode.Weighted)
        ]);
        await exercises.QueueAsync(new PendingCustomExercise(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            null,
            "My exercise",
            BodyPart.Chest,
            TrackingMode.Weighted,
            null,
            null,
            null,
            new DateTimeOffset(2026, 8, 24, 4, 0, 0, TimeSpan.Zero)));

        var risk = await source.GetRiskAsync();

        Assert.True(risk.HasActiveWorkout);
        Assert.Equal(1, risk.UnsyncedWorkoutChangeCount);
        Assert.Equal(1, risk.UnsyncedCustomExerciseCount);
        Assert.True(risk.HasDataAtRisk);
    }

    [Fact]
    public void Unknown_risk_warning_is_plain_and_localized()
    {
        var english = MobileResources.ForCulture(
            System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var thai = MobileResources.ForCulture(
            System.Globalization.CultureInfo.GetCultureInfo("th-TH"));

        Assert.Equal("Saved data couldn’t be checked", english.SignOutUnknownTitle);
        Assert.Contains("exists only on this device", english.SignOutUnknownMessage);
        Assert.Equal("ตรวจสอบข้อมูลที่บันทึกไว้ไม่ได้", thai.SignOutUnknownTitle);
        Assert.Contains("อยู่ในเครื่องนี้เท่านั้น", thai.SignOutUnknownMessage);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _workoutDatabasePath, _exerciseDatabasePath })
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var candidate = path + suffix;
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    private static AuthenticationFixture Authentication()
    {
        var storage = new MemoryTokenStorage();
        var store = new MobileTokenStore(storage);
        var identity = new RecordingIdentity();
        var cleaner = new RecordingCleaner();
        var boundary = new AccountSessionBoundary();
        return new AuthenticationFixture(
            new AuthGateCoordinator(
                store,
                identity,
                cleaner,
                boundary,
                new TestDeviceNameProvider(),
                new OnlineConnectivity(),
                TimeProvider.System),
            identity,
            cleaner,
            boundary);
    }

    private sealed record AuthenticationFixture(
        AuthGateCoordinator Coordinator,
        RecordingIdentity Identity,
        RecordingCleaner Cleaner,
        AccountSessionBoundary Boundary);

    private sealed class FixedRiskSource(ProfileSignOutRisk risk) : IProfileSignOutRiskSource
    {
        public Task<ProfileSignOutRisk> GetRiskAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(risk);
    }

    private sealed class SequenceRiskSource(params ProfileSignOutRisk[] risks)
        : IProfileSignOutRiskSource
    {
        private int _index;

        public Task<ProfileSignOutRisk> GetRiskAsync(
            CancellationToken cancellationToken = default)
        {
            var index = Math.Min(
                Interlocked.Increment(ref _index) - 1,
                risks.Length - 1);
            return Task.FromResult(risks[index]);
        }
    }

    private sealed class FailingRiskSource : IProfileSignOutRiskSource
    {
        public Task<ProfileSignOutRisk> GetRiskAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("database unavailable");
    }

    private sealed class RiskAwareConfirmation(bool confirmClear, bool confirmAtRisk)
        : IProfileSignOutConfirmation
    {
        public List<bool> DestructivePrompts { get; } = [];

        public Task<bool> ConfirmAsync(
            ProfileSignOutRisk risk,
            CancellationToken cancellationToken = default)
        {
            DestructivePrompts.Add(risk.RequiresDataLossConfirmation);
            return Task.FromResult(
                risk.RequiresDataLossConfirmation ? confirmAtRisk : confirmClear);
        }
    }

    private sealed class SequenceConfirmation(params bool[] answers)
        : IProfileSignOutConfirmation
    {
        private int _index;
        public List<int> WarningStrengths { get; } = [];

        public Task<bool> ConfirmAsync(
            ProfileSignOutRisk risk,
            CancellationToken cancellationToken = default)
        {
            WarningStrengths.Add(
                risk.InspectionFailed ? 2 : risk.HasDataAtRisk ? 1 : 0);
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, answers.Length - 1);
            return Task.FromResult(answers[index]);
        }
    }

    private sealed class RecordingIdentity : IIdentitySessionApi
    {
        private readonly TaskCompletionSource _logoutEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseLogout = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _gateLogout;
        public int LogoutCalls { get; private set; }
        public Task LogoutEntered => _logoutEntered.Task;
        public void GateLogout() => _gateLogout = true;
        public void ReleaseLogout() => _releaseLogout.TrySetResult();
        public Task LoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RegisterAndLoginAsync(string email, string password, string deviceName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task RefreshAsync(string deviceName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public async Task LogoutAsync(CancellationToken cancellationToken = default)
        {
            LogoutCalls++;
            if (!_gateLogout) return;
            _logoutEntered.TrySetResult();
            await _releaseLogout.Task.WaitAsync(cancellationToken);
        }
        public Task<IPreparedIdentityLogout> PrepareLogoutAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IPreparedIdentityLogout>(
                new PreparedIdentityLogout(LogoutAsync));
        }
    }

    private sealed class RecordingCleaner : IMobilePrivateDataCleaner
    {
        public int ClearCount { get; private set; }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryTokenStorage : IMobileTokenStorage
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class TestDeviceNameProvider : IDeviceNameProvider
    {
        public string DeviceName => "Test iPhone";
    }

    private sealed class OnlineConnectivity : IConnectivityService
    {
        public bool IsOnline => true;
        public event EventHandler? ConnectivityChanged { add { } remove { } }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow =>
            new(2026, 8, 24, 4, 0, 0, TimeSpan.Zero);
    }
}
