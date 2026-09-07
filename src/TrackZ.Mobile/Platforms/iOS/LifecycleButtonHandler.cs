#if IOS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace TrackZ.Mobile.Presentation;

// MAUI 10.0.20's Button.LayoutButton dereferences a missing platform button
// when UIKit delivers a queued layout after navigation has detached its handler.
internal sealed class LifecycleButtonHandler : ButtonHandler
{
    // WrapperView retains its layout through a weak reference.
    private HandlerAwareLayout? _layout;

    protected override void SetupContainer()
    {
        base.SetupContainer();
        if (ContainerView is WrapperView wrapper && VirtualView is ICrossPlatformLayout layout)
        {
            var button = VirtualView;
            _layout = new HandlerAwareLayout(layout,
                () => ReferenceEquals(button.Handler, this) && PlatformView is not null);
            ((ICrossPlatformLayoutBacking)wrapper).CrossPlatformLayout = _layout;
        }
    }
}
#endif
