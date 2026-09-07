using Microsoft.Maui.Graphics;

namespace TrackZ.Mobile.Presentation;

internal sealed class HandlerAwareLayout(ICrossPlatformLayout layout, Func<bool> isAttached) : ICrossPlatformLayout
{
    public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) =>
        isAttached() ? layout.CrossPlatformMeasure(widthConstraint, heightConstraint) : Size.Zero;

    public Size CrossPlatformArrange(Rect bounds) =>
        isAttached() ? layout.CrossPlatformArrange(bounds) : Size.Zero;
}
