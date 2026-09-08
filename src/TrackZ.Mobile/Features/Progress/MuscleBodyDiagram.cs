using TrackZ.Domain.Exercises;
using TrackZ.Domain.Muscles;

namespace TrackZ.Mobile.Features.Progress;

/// <summary>Native SVG muscle coverage whose fill, outline and hit testing share parsed PathF geometry.</summary>
internal sealed class MuscleBodyDiagram(
    MuscleCoverageReport report,
    MuscleSvgDocument document,
    BodyPart? group = null,
    string? focused = null) : IDrawable
{
    internal sealed record ShapeStatus(string RegionId, PathF Path, MuscleTrainingStatus Status);

    internal static IReadOnlyList<ShapeStatus> ResolveShapeStatuses(MuscleSvgDocument source, MuscleCoverageReport report)
    {
        var statuses = report.Regions.ToDictionary(region => region.Id, region => region.Status, StringComparer.Ordinal);
        return source.Shapes
            .Where(shape => shape.RegionId is not null && statuses.ContainsKey(shape.RegionId))
            .Select(shape => new ShapeStatus(shape.RegionId!, shape.Path, statuses[shape.RegionId!]))
            .ToArray();
    }

    internal static Color StatusColor(MuscleTrainingStatus status) => Color.FromArgb(status switch
    {
        MuscleTrainingStatus.Primary => "#C8FF3D",
        MuscleTrainingStatus.Secondary => "#69B5BA",
        _ => "#626B72"
    });

    internal static Color OutlineColor(string? selectedRegion, string? shapeRegion) =>
        selectedRegion is not null && selectedRegion == shapeRegion
            ? Color.FromArgb("#F5F7F8")
            : Color.FromArgb("#111619");

    internal static RectF SourceBounds(MuscleSvgDocument source, BodyPart? selectedGroup, string? selectedRegion)
    {
        if (selectedGroup is null && selectedRegion is null) return source.ViewBox;
        var selected = source.VisibleTrainingShapes(selectedGroup, selectedRegion);
        if (selected.Count == 0) return source.ViewBox;
        var union = selected.Select(shape => shape.Bounds).Aggregate(Union);
        var xPadding = selectedRegion is null ? 54f : 64f;
        var yPadding = selectedRegion is null ? 62f : 72f;
        return Clamp(new RectF(union.X - xPadding, union.Y - yPadding,
            union.Width + xPadding * 2, union.Height + yPadding * 2), source.ViewBox);
    }

    internal static PointF SvgToView(MuscleSvgDocument source, PointF point, RectF bounds, BodyPart? selectedGroup, string? selectedRegion)
    {
        var transform = Transform(bounds, SourceBounds(source, selectedGroup, selectedRegion));
        return new PointF(transform.X + point.X * transform.Scale, transform.Y + point.Y * transform.Scale);
    }

    internal static string? HitTest(MuscleSvgDocument source, PointF point, RectF bounds, BodyPart? selectedGroup, string? selectedRegion = null)
    {
        var sourceBounds = SourceBounds(source, selectedGroup, selectedRegion);
        var transform = Transform(bounds, sourceBounds);
        if (transform.Scale <= 0) return null;
        var svgPoint = new PointF((point.X - transform.X) / transform.Scale, (point.Y - transform.Y) / transform.Scale);
        if (!sourceBounds.Contains(svgPoint)) return null;
        foreach (var shape in source.VisibleTrainingShapes(selectedGroup, selectedRegion).Reverse())
            if (shape.Contains(svgPoint)) return shape.RegionId;
        return null;
    }

    public void Draw(ICanvas canvas, RectF bounds)
    {
        canvas.FillColor = Color.FromArgb("#090D0E");
        canvas.FillRectangle(bounds);

        var sourceBounds = SourceBounds(document, group, focused);
        var transform = Transform(bounds, sourceBounds);
        if (transform.Scale <= 0) return;
        var selected = document.VisibleTrainingShapes(group, focused).ToHashSet();
        var statuses = report.Regions.ToDictionary(region => region.Id, region => region.Status, StringComparer.Ordinal);
        var deepFocus = focused == "deep-core";

        canvas.SaveState();
        canvas.ClipRectangle(bounds);
        canvas.Translate(transform.X, transform.Y);
        canvas.Scale(transform.Scale, transform.Scale);

        foreach (var shape in document.Shapes.Where(shape => shape.DetailFor is null && (deepFocus ? shape.IsDeep : !shape.IsDeep)))
        {
            var status = shape.RegionId is not null && selected.Contains(shape) && statuses.TryGetValue(shape.RegionId, out var value)
                ? value
                : MuscleTrainingStatus.NoRecord;
            var color = shape.RegionId is null ? Color.FromArgb("#353D42") : StatusColor(status);
            canvas.SetFillPaint(Gradient(color, shape.RegionId is null), shape.Bounds);
            canvas.FillPath(shape.Path, WindingMode.EvenOdd);
            var isFocusedShape = focused is not null && focused == shape.RegionId;
            canvas.StrokeColor = OutlineColor(focused, shape.RegionId);
            canvas.StrokeSize = (isFocusedShape ? 2.1f : .7f) / transform.Scale;
            canvas.DrawPath(shape.Path);
        }

        if (!deepFocus)
            DrawDetails(canvas, selected);
        canvas.RestoreState();
    }

    private void DrawDetails(ICanvas canvas, IReadOnlySet<MuscleSvgShape> selected)
    {
        foreach (var detail in document.Shapes.Where(shape => shape.DetailFor is not null))
        foreach (var target in selected.Where(shape => shape.RegionId == detail.DetailFor && shape.Side == detail.Side))
        {
            canvas.SaveState();
            canvas.ClipPath(target.Path, WindingMode.EvenOdd);
            canvas.StrokeColor = Color.FromArgb("#E8EEF0").WithAlpha(.24f);
            canvas.StrokeSize = .9f;
            canvas.DrawPath(detail.Path);
            canvas.RestoreState();
        }
    }

    private static LinearGradientPaint Gradient(Color color, bool decorative)
    {
        var highlight = decorative ? Color.FromArgb("#465057") : Mix(color, Colors.White, .14f);
        var shadow = decorative ? Color.FromArgb("#252C30") : Mix(color, Color.FromArgb("#182024"), .20f);
        return new LinearGradientPaint(
            [new PaintGradientStop(0, highlight), new PaintGradientStop(1, shadow)],
            new Microsoft.Maui.Graphics.Point(0, 0),
            new Microsoft.Maui.Graphics.Point(1, 1));
    }

    private static Color Mix(Color left, Color right, float amount) => new(
        left.Red + (right.Red - left.Red) * amount,
        left.Green + (right.Green - left.Green) * amount,
        left.Blue + (right.Blue - left.Blue) * amount,
        1);

    private static RectF Union(RectF left, RectF right)
    {
        var x = Math.Min(left.Left, right.Left);
        var y = Math.Min(left.Top, right.Top);
        return new RectF(x, y, Math.Max(left.Right, right.Right) - x, Math.Max(left.Bottom, right.Bottom) - y);
    }

    private static RectF Clamp(RectF source, RectF limits)
    {
        var left = Math.Max(limits.Left, source.Left);
        var top = Math.Max(limits.Top, source.Top);
        var right = Math.Min(limits.Right, source.Right);
        var bottom = Math.Min(limits.Bottom, source.Bottom);
        return new RectF(left, top, right - left, bottom - top);
    }

    private static (float X, float Y, float Scale) Transform(RectF bounds, RectF source)
    {
        var scale = source.Width <= 0 || source.Height <= 0 ? 0 : Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
        return (bounds.X + (bounds.Width - source.Width * scale) / 2 - source.X * scale,
            bounds.Y + (bounds.Height - source.Height * scale) / 2 - source.Y * scale,
            scale);
    }
}
