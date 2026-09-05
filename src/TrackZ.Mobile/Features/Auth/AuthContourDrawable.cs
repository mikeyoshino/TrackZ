namespace TrackZ.Mobile.Features.Auth;

internal sealed class AuthContourDrawable(bool isForeground) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            return;

        canvas.SaveState();
        canvas.StrokeColor = isForeground
            ? Color.FromRgba(200, 255, 61, 34)
            : Color.FromRgba(105, 145, 50, 28);
        canvas.StrokeSize = isForeground ? 0.9f : 0.7f;
        canvas.StrokeLineCap = LineCap.Round;

        DrawLeftContours(canvas, dirtyRect);
        DrawRightContours(canvas, dirtyRect);
        DrawBottomContours(canvas, dirtyRect);
        canvas.RestoreState();
    }

    private void DrawLeftContours(ICanvas canvas, RectF bounds)
    {
        for (var index = 0; index < 5; index++)
        {
            var inset = index * bounds.Width * 0.045f;
            var path = new PathF();
            path.MoveTo(-bounds.Width * 0.18f + inset, bounds.Height * 0.12f);
            path.CurveTo(
                bounds.Width * 0.16f + inset, bounds.Height * 0.16f,
                -bounds.Width * 0.06f + inset, bounds.Height * 0.34f,
                bounds.Width * 0.10f + inset, bounds.Height * 0.43f);
            path.CurveTo(
                bounds.Width * 0.22f + inset, bounds.Height * 0.51f,
                -bounds.Width * 0.01f + inset, bounds.Height * 0.61f,
                bounds.Width * 0.08f + inset, bounds.Height * 0.72f);
            canvas.DrawPath(path);
        }
    }

    private void DrawRightContours(ICanvas canvas, RectF bounds)
    {
        for (var index = 0; index < 5; index++)
        {
            var inset = index * bounds.Width * 0.045f;
            var path = new PathF();
            path.MoveTo(bounds.Width * 1.12f - inset, bounds.Height * 0.08f);
            path.CurveTo(
                bounds.Width * 0.78f - inset, bounds.Height * 0.18f,
                bounds.Width * 1.08f - inset, bounds.Height * 0.31f,
                bounds.Width * 0.90f - inset, bounds.Height * 0.40f);
            path.CurveTo(
                bounds.Width * 0.78f - inset, bounds.Height * 0.49f,
                bounds.Width * 1.03f - inset, bounds.Height * 0.59f,
                bounds.Width * 0.91f - inset, bounds.Height * 0.70f);
            canvas.DrawPath(path);
        }
    }

    private void DrawBottomContours(ICanvas canvas, RectF bounds)
    {
        for (var index = 0; index < 4; index++)
        {
            var y = bounds.Height * (0.83f + index * 0.035f);
            var path = new PathF();
            path.MoveTo(-bounds.Width * 0.08f, y);
            path.CurveTo(
                bounds.Width * 0.24f, y - bounds.Height * 0.05f,
                bounds.Width * 0.48f, y + bounds.Height * 0.04f,
                bounds.Width * 0.72f, y - bounds.Height * 0.02f);
            path.CurveTo(
                bounds.Width * 0.87f, y - bounds.Height * 0.06f,
                bounds.Width * 1.02f, y + bounds.Height * 0.02f,
                bounds.Width * 1.10f, y);
            canvas.DrawPath(path);
        }
    }
}
