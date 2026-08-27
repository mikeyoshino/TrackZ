using TrackZ.Mobile.Features.Gamification;
using TrackZ.Mobile.Features.Localization;

namespace TrackZ.Mobile.Features.Profile;

public sealed class MauiProfileSignOutConfirmation(
    MobileTextSet text,
    GamificationTextSet gamificationText) : IProfileSignOutConfirmation
{
    public async Task<bool> ConfirmAsync(
        ProfileSignOutRisk risk,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = Application.Current?.Windows.FirstOrDefault()?.Page
            ?? throw new InvalidOperationException(
                "No active window is available for sign-out confirmation.");
        var confirmed = risk.InspectionFailed
            ? await page.DisplayAlertAsync(
                text.SignOutUnknownTitle,
                text.SignOutUnknownMessage,
                text.SignOutAndDelete,
                text.StaySignedIn)
            : risk.HasDataAtRisk
            ? await page.DisplayAlertAsync(
                text.SignOutDataTitle,
                text.SignOutDataMessage,
                text.SignOutAndDelete,
                text.StaySignedIn)
            : await page.DisplayAlertAsync(
                text.SignOutTitle,
                text.SignOutMessage,
                gamificationText.SignOut,
                text.StaySignedIn);
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }
}
