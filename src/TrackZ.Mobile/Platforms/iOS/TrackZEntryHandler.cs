using CoreGraphics;
using Foundation;
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
    private NSObject? _keyboardFrameObserver;

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
        platformView.EditingDidBegin += OnEditingDidBegin;
        _keyboardFrameObserver = UIKeyboard.Notifications.ObserveDidChangeFrame((_, _) => RevealFocusedInput());
    }

    private void DismissKeyboard(object? sender, EventArgs args) => PlatformView?.ResignFirstResponder();

    private void OnEditingDidBegin(object? sender, EventArgs args) => RevealFocusedInput();

    private void RevealFocusedInput()
    {
        // The keyboard safe-area resize can happen after editing begins. Re-evaluate
        // on its final frame too; otherwise an already-focused field remains clipped.
        if (VirtualView is not Entry view) return;
        var expectedField = PlatformView;
        view.Dispatcher.Dispatch(() =>
        {
            if (PlatformView is not { IsFirstResponder: true } field || field.Window is null
                || !ReferenceEquals(field, expectedField)) return;
            field.Window.LayoutIfNeeded();
            for (var parent = field.Superview; parent is not null; parent = parent.Superview)
            {
                if (parent is not UIScrollView scroll) continue;
                // MauiScrollView suppresses native ScrollRectToVisible while its
                // keyboard manager is active. Move only the vertical offset;
                // MakeVisible can also produce an unwanted horizontal offset.
                var target = field.ConvertRectToView(field.Bounds, scroll);
                var offset = (double)scroll.ContentOffset.Y;
                var visibleTop = offset + (double)scroll.AdjustedContentInset.Top;
                var visibleBottom = offset + (double)scroll.Bounds.Height
                    - (double)scroll.AdjustedContentInset.Bottom;
                if (target.Bottom + 16 > visibleBottom)
                    offset += (double)target.Bottom + 16 - visibleBottom;
                else if (target.Top - 16 < visibleTop)
                    offset -= visibleTop - ((double)target.Top - 16);
                var min = -(double)scroll.AdjustedContentInset.Top;
                var max = Math.Max(min, (double)(scroll.ContentSize.Height - scroll.Bounds.Height
                    + scroll.AdjustedContentInset.Bottom));
                scroll.SetContentOffset(new CGPoint(scroll.ContentOffset.X, Math.Clamp(offset, min, max)),
                    !UIAccessibility.IsReduceMotionEnabled);
                break;
            }
        });
    }

    protected override void DisconnectHandler(MauiTextField platformView)
    {
        platformView.EditingDidBegin -= OnEditingDidBegin;
        _keyboardFrameObserver?.Dispose();
        _keyboardFrameObserver = null;
        platformView.InputAccessoryView = null;
        if (_dismiss is not null) _dismiss.TouchUpInside -= DismissKeyboard;
        _dismiss?.Dispose();
        _accessory?.Dispose();
        _dismiss = null;
        _accessory = null;
        base.DisconnectHandler(platformView);
    }
}
