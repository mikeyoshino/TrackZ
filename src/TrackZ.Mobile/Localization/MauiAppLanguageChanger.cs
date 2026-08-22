using TrackZ.Mobile.Features.Localization;
using TrackZ.Mobile.Identity;

namespace TrackZ.Mobile.Localization;

public sealed class MauiAppLanguageChanger : IAppLanguageChanger
{
    private readonly IAppLanguageStore _store;
    private readonly ILocalizedUiHost _ui;
    private readonly AuthGateCoordinator _authentication;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _isChanging;

    public MauiAppLanguageChanger(
        IAppLanguageStore store,
        ILocalizedUiHost ui,
        AuthGateCoordinator authentication)
    {
        _store = store;
        _ui = ui;
        _authentication = authentication;
        Current = store.Read();
    }

    public AppLanguage Current { get; private set; }
    public bool IsChanging => Volatile.Read(ref _isChanging) != 0;

    public async Task ChangeAsync(
        AppLanguage language,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (language == Current) return;
            Volatile.Write(ref _isChanging, 1);
            var previous = Current;
            var rootTabRoute = _ui.CurrentRootTabRoute;
            LocalizedUiInstallation? candidate = null;
            try
            {
                _store.Write(language);
                AppLanguageCulture.Apply(language);
                var authentication = _authentication.Snapshot;
                candidate = _ui.Prepare(authentication);
                if (_authentication.Snapshot != authentication)
                {
                    candidate.Dispose();
                    candidate = _ui.Prepare(_authentication.Snapshot);
                }

                await _ui.InstallAsync(candidate, rootTabRoute, cancellationToken);
                candidate = null;
                Current = language;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                candidate?.Dispose();
                RollBack(previous);
                throw;
            }
            catch (Exception)
            {
                candidate?.Dispose();
                RollBack(previous);
                throw new AppLanguageChangeException();
            }
            finally
            {
                Volatile.Write(ref _isChanging, 0);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RollBack(AppLanguage previous)
    {
        try
        {
            _store.Write(previous);
        }
        finally
        {
            AppLanguageCulture.Apply(previous);
            Current = previous;
        }
    }
}
