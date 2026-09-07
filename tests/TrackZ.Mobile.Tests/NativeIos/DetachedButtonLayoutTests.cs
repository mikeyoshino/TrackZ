using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using TrackZ.Mobile.Presentation;

namespace TrackZ.Mobile.Tests.NativeIos;

public class DetachedButtonLayoutTests
{
    [Fact]
    public void Queued_layout_after_handler_disconnect_does_not_call_native_button()
    {
        var attached = true;
        var native = new RecordingLayout();
        var layout = new HandlerAwareLayout(native, () => attached);
        var bounds = new Rect(0, 0, 160, 80);
        Assert.Equal(new Size(160, 80), layout.CrossPlatformArrange(bounds));
        Assert.Equal(new Size(120, 44), layout.CrossPlatformMeasure(160, 80));
        attached = false;
        native.Detached = true;
        Assert.Equal(Size.Zero, layout.CrossPlatformArrange(bounds));
        Assert.Equal(Size.Zero, layout.CrossPlatformMeasure(160, 80));
        Assert.Equal(1, native.ArrangeCount);
        Assert.Equal(1, native.MeasureCount);
    }

    private sealed class RecordingLayout : ICrossPlatformLayout
    {
        public bool Detached { get; set; }
        public int ArrangeCount { get; private set; }
        public int MeasureCount { get; private set; }
        public Size CrossPlatformArrange(Rect bounds)
        {
            if (Detached) throw new NullReferenceException("Native button has disconnected");
            ArrangeCount++;
            return bounds.Size;
        }
        public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint)
        {
            if (Detached) throw new NullReferenceException("Native button has disconnected");
            MeasureCount++;
            return new Size(120, 44);
        }
    }
}
