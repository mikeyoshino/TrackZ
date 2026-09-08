using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Maui.Storage;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Muscles;

namespace TrackZ.Mobile.Features.Progress;

/// <summary>Validated, cached native geometry from the bundled flat anatomy SVG.</summary>
internal sealed class MuscleSvgDocument
{
    private const string AssetName = "muscles/body.svg";
    private const float HitTestFlatness = .35f;
    private static readonly object Sync = new();
    private static Task<MuscleSvgDocument>? _load;

    private MuscleSvgDocument(RectF viewBox, IReadOnlyList<MuscleSvgShape> shapes)
    {
        ViewBox = viewBox;
        Shapes = shapes;
        TrainingRegionIds = shapes
            .Where(shape => shape.RegionId is not null)
            .Select(shape => shape.RegionId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal RectF ViewBox { get; }
    internal IReadOnlyList<MuscleSvgShape> Shapes { get; }
    internal IReadOnlyList<string> TrainingRegionIds { get; }

    internal static Task<MuscleSvgDocument> LoadAsync(CancellationToken cancellationToken)
    {
        Task<MuscleSvgDocument> load;
        lock (Sync) load = _load ??= LoadCoreAsync();
        return load.WaitAsync(cancellationToken);
    }

    private static async Task<MuscleSvgDocument> LoadCoreAsync()
    {
        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync(AssetName);
        using var reader = new StreamReader(stream);
        return Parse(await reader.ReadToEndAsync(), MuscleCatalog.Regions.Select(region => region.Id));
    }

    internal static MuscleSvgDocument Parse(string svg, IEnumerable<string> requiredRegionIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);
        ArgumentNullException.ThrowIfNull(requiredRegionIds);

        var required = requiredRegionIds.ToHashSet(StringComparer.Ordinal);
        XDocument xml;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 4_000_000
            };
            using var text = new StringReader(svg);
            using var reader = XmlReader.Create(text, settings);
            xml = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            throw new InvalidDataException("The bundled muscle SVG is not valid XML.", exception);
        }

        var root = xml.Root;
        if (root is null || root.Name.LocalName != "svg")
            throw new InvalidDataException("The bundled muscle SVG has no svg root.");
        if (root.Descendants().Any(element => element.Name.LocalName is "script" or "image" or "use" or "foreignObject" or "g"))
            throw new InvalidDataException("The bundled muscle SVG must contain flat, local paths only.");

        var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
        var shapes = new List<MuscleSvgShape>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in root.Elements().Where(element => element.Name.LocalName == "path"))
        {
            var id = element.Attribute("id")?.Value;
            var definition = element.Attribute("d")?.Value;
            var regionId = element.Attribute("data-region")?.Value;
            var detailFor = element.Attribute("data-detail-for")?.Value;
            var side = element.Attribute("data-side")?.Value;
            var deep = string.Equals(element.Attribute("data-layer")?.Value, "deep", StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new InvalidDataException($"The bundled muscle SVG has a missing or duplicate path id '{id}'.");
            if (string.IsNullOrWhiteSpace(definition))
                throw new InvalidDataException($"SVG path '{id}' has no geometry.");
            if (element.Attribute("transform") is not null)
                throw new InvalidDataException($"SVG path '{id}' uses a transform; shared absolute geometry is required.");
            if (regionId is not null && detailFor is not null)
                throw new InvalidDataException($"SVG path '{id}' cannot be both a training and detail path.");
            ValidateRegion(regionId, required, id);
            ValidateRegion(detailFor, required, id);

            PathF path;
            try { path = PathBuilder.Build(definition); }
            catch (Exception exception)
            { throw new InvalidDataException($"SVG path '{id}' has invalid path data.", exception); }
            if (path.Count == 0)
                throw new InvalidDataException($"SVG path '{id}' has empty path data.");

            var bounds = path.GetBoundsByFlattening(HitTestFlatness);
            shapes.Add(new MuscleSvgShape(id, regionId, detailFor, side, deep, path, bounds, Flatten(path)));
        }

        var present = shapes.Where(shape => shape.RegionId is not null).Select(shape => shape.RegionId!).ToHashSet(StringComparer.Ordinal);
        var missing = required.Where(regionId => !present.Contains(regionId)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("The bundled muscle SVG is missing regions: " + string.Join(", ", missing));
        return new MuscleSvgDocument(viewBox, shapes);
    }

    internal IReadOnlyList<MuscleSvgShape> VisibleTrainingShapes(BodyPart? group, string? focused)
    {
        if (focused is not null)
            return Shapes.Where(shape => shape.RegionId == focused && shape.IsDeep == (focused == "deep-core")).ToArray();

        return Shapes.Where(shape => shape.RegionId is not null && !shape.IsDeep &&
            (group is null || MuscleCatalog.Regions.Single(region => region.Id == shape.RegionId).BodyPart == group)).ToArray();
    }

    internal string? RegionAtPoint(PointF point, bool includeDeep)
    {
        foreach (var shape in Shapes.Reverse())
            if (shape.RegionId is not null && (includeDeep || !shape.IsDeep) && shape.Contains(point))
                return shape.RegionId;
        return null;
    }

    private static RectF ParseViewBox(string? value)
    {
        var parts = value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length != 4 || !parts.All(part => float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            throw new InvalidDataException("The bundled muscle SVG has an invalid viewBox.");
        var numbers = parts.Select(part => float.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        if (numbers[2] <= 0 || numbers[3] <= 0)
            throw new InvalidDataException("The bundled muscle SVG viewBox must have positive dimensions.");
        return new RectF(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    private static void ValidateRegion(string? regionId, IReadOnlySet<string> allowed, string pathId)
    {
        if (regionId is not null && !allowed.Contains(regionId))
            throw new InvalidDataException($"SVG path '{pathId}' references unknown region '{regionId}'.");
    }

    private static IReadOnlyList<IReadOnlyList<PointF>> Flatten(PathF path) =>
        path.GetFlattenedPath(HitTestFlatness, includeSubPaths: true)
            .Separate()
            .Select(subPath => (IReadOnlyList<PointF>)subPath.Points.ToArray())
            .Where(points => points.Count >= 3)
            .ToArray();
}

internal sealed record MuscleSvgShape(
    string Id,
    string? RegionId,
    string? DetailFor,
    string? Side,
    bool IsDeep,
    PathF Path,
    RectF Bounds,
    IReadOnlyList<IReadOnlyList<PointF>> HitPolygons)
{
    internal bool Contains(PointF point)
    {
        var inside = false;
        foreach (var polygon in HitPolygons)
            if (Contains(point, polygon)) inside = !inside;
        return inside;
    }

    private static bool Contains(PointF point, IReadOnlyList<PointF> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var current = polygon[i];
            var previous = polygon[j];
            if ((current.Y > point.Y) != (previous.Y > point.Y) &&
                point.X < (previous.X - current.X) * (point.Y - current.Y) / (previous.Y - current.Y) + current.X)
                inside = !inside;
        }
        return inside;
    }
}
