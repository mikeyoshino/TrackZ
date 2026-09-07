using CoreGraphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
using System.Globalization;

namespace TrackZ.Mobile.Presentation;

/// <summary>Use the app's keyboard dismissal control instead of iOS's blue Done accessory.</summary>
public sealed class TrackZEntryHandler : EntryHandler
{
    private UIView? _accessory;
    private UIButton? _dismiss;

    protected override MauiTextField CreatePlatformView() => new()
    {
        BorderStyle = UITextBorderStyle.None,
        ClipsToBounds = true,
        TintColor = UIColor.FromRGB(200, 255, 61)
    };

    protected override void ConnectHandler(MauiTextField platformView)
    {
        base.ConnectHandler(platformView);
        _accessory = new UIView(new CGRect(0, 0, 320, 44))
        {
            BackgroundColor = UIColor.FromRGB(21, 26, 30),
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth
        };
        _dismiss = new UIButton(UIButtonType.Custom)
        {
            Frame = new CGRect(208, 0, 104, 44),
            AutoresizingMask = UIViewAutoresizing.FlexibleLeftMargin
        };
        _dismiss.SetTitle(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "th" ? "ซ่อนแป้นพิมพ์" : "Hide keyboard", UIControlState.Normal);
        _dismiss.SetTitleColor(UIColor.FromRGB(200, 255, 61), UIControlState.Normal);
        if (UIFont.SystemFontOfSize(14) is { } font) _dismiss.TitleLabel.Font = font;
        _dismiss.TouchUpInside += DismissKeyboard;
        _accessory.AddSubview(_dismiss);
        platformView.InputAccessoryView = _accessory;
    }

    private void DismissKeyboard(object? sender, EventArgs args) => PlatformView?.ResignFirstResponder();

    protected override void DisconnectHandler(MauiTextField platformView)
    {
        platformView.InputAccessoryView = null;
        if (_dismiss is not null) _dismiss.TouchUpInside -= DismissKeyboard;
        _dismiss?.Dispose();
        _accessory?.Dispose();
        _dismiss = null;
        _accessory = null;
        base.DisconnectHandler(platformView);
    }
}
