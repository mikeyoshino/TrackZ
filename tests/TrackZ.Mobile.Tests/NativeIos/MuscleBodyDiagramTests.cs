using Microsoft.Maui.Graphics;
using System.Reflection;
using TrackZ.Domain.Exercises;
using TrackZ.Domain.Muscles;
using TrackZ.Mobile.Features.Progress;

namespace TrackZ.Mobile.Tests.NativeIos;

public sealed class MuscleBodyDiagramTests
{
    private const string CurvedFixture = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
          <path id="outline" data-side="front" d="M2 2 H98 V98 H2 Z" />
          <path id="quad" data-region="quads" data-side="front"
                d="M10 50 C10 10 90 10 90 50 C90 90 10 90 10 50 Z" />
          <path id="fiber" data-detail-for="quads" data-side="front"
                d="M0 50 Q50 25 100 50" />
        </svg>
        """;

    [Fact]
    public void Parser_builds_native_paths_for_curved_training_and_detail_shapes()
    {
        var document = MuscleSvgDocument.Parse(CurvedFixture, ["quads"]);

        Assert.Equal(new RectF(0, 0, 100, 100), document.ViewBox);
        Assert.Equal(3, document.Shapes.Count);
        Assert.Contains(PathOperation.Cubic, document.Shapes.Single(shape => shape.Id == "quad").Path.SegmentTypes);
        Assert.Contains(PathOperation.Quad, document.Shapes.Single(shape => shape.Id == "fiber").Path.SegmentTypes);
    }

    [Fact]
    public void Bundled_svg_parses_every_source_path_and_training_region()
    {
        var document = MuscleSvgDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TrackZ.Mobile", "Resources", "Raw", "muscles", "body.svg")),
            MuscleCatalog.Regions.Select(region => region.Id));

        Assert.Equal(new RectF(0, 0, 1440, 1450), document.ViewBox);
        Assert.Equal(310, document.Shapes.Count);
        Assert.Equal(
            MuscleCatalog.Regions.Select(region => region.Id).Order(),
            document.TrainingRegionIds.Order());
    }

    [Fact]
    public void Bundled_svg_preserves_reference_curve_geometry_after_native_parsing()
    {
        var document = BundledDocument();
        var oblique = document.Shapes.Single(shape => shape.Id == "muscle-1").Bounds;
        var quadriceps = document.Shapes.Single(shape => shape.Id == "muscle-42").Bounds;

        Assert.InRange(oblique.Left, 437, 439);
        Assert.InRange(oblique.Right, 469, 470);
        Assert.InRange(oblique.Top, 416, 418);
        Assert.InRange(oblique.Bottom, 444, 446);
        Assert.InRange(quadriceps.Left, 243, 245);
        Assert.InRange(quadriceps.Right, 308, 310);
        Assert.InRange(quadriceps.Top, 666, 668);
        Assert.InRange(quadriceps.Bottom, 935, 940);
        Assert.Equal("quads", document.RegionAtPoint(new PointF(292, 800), includeDeep: false));
        Assert.Equal("glutes", document.RegionAtPoint(new PointF(1035, 710), includeDeep: false));
    }

    [Fact]
    public void Overview_decorative_shapes_do_not_receive_the_focused_white_outline()
    {
        Assert.Equal(Color.FromArgb("#111619"), MuscleBodyDiagram.OutlineColor(null, null));
        Assert.Equal(Color.FromArgb("#111619"), MuscleBodyDiagram.OutlineColor("quads", null));
        Assert.Equal(Color.FromArgb("#F5F7F8"), MuscleBodyDiagram.OutlineColor("quads", "quads"));
    }

    [Fact]
    public void Parser_rejects_unknown_training_regions()
    {
        var svg = CurvedFixture.Replace("data-region=\"quads\"", "data-region=\"not-a-region\"");

        var error = Assert.Throws<InvalidDataException>(() => MuscleSvgDocument.Parse(svg, ["quads"]));

        Assert.Contains("not-a-region", error.Message);
    }

    [Fact]
    public void Parser_rejects_a_missing_required_region()
    {
        var error = Assert.Throws<InvalidDataException>(() => MuscleSvgDocument.Parse(CurvedFixture, ["quads", "hamstrings"]));

        Assert.Contains("hamstrings", error.Message);
    }

    [Fact]
    public void Hit_testing_uses_the_curve_instead_of_its_rectangular_bounds()
    {
        var document = MuscleSvgDocument.Parse(CurvedFixture, ["quads"]);

        Assert.Equal("quads", document.RegionAtPoint(new PointF(50, 50), includeDeep: false));
        Assert.Null(document.RegionAtPoint(new PointF(12, 12), includeDeep: false));
    }

    [Fact]
    public void Deep_core_uses_even_odd_hit_testing_and_only_appears_in_its_focus()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <path id="quad" data-region="quads" data-side="front" d="M5 5 H20 V20 H5 Z" />
              <path id="deep" data-region="deep-core" data-side="front" data-layer="deep"
                    d="M30 30 H90 V90 H30 Z M45 45 H75 V75 H45 Z" />
            </svg>
            """;
        var document = MuscleSvgDocument.Parse(svg, ["quads", "deep-core"]);

        Assert.DoesNotContain(document.VisibleTrainingShapes(null, null), shape => shape.RegionId == "deep-core");
        Assert.Contains(document.VisibleTrainingShapes(BodyPart.Core, "deep-core"), shape => shape.RegionId == "deep-core");
        Assert.Equal("deep-core", document.RegionAtPoint(new PointF(35, 35), includeDeep: true));
        Assert.Null(document.RegionAtPoint(new PointF(60, 60), includeDeep: true));
    }

    [Fact]
    public void Status_resolution_keeps_the_exact_parsed_path_for_fill_outline_and_hit_testing()
    {
        var document = MuscleSvgDocument.Parse(CurvedFixture, ["quads"]);
        var parsed = document.Shapes.Single(shape => shape.RegionId == "quads");
        var resolved = MuscleBodyDiagram.ResolveShapeStatuses(document, Report("quads", primarySets: 3)).Single();

        Assert.Same(parsed.Path, resolved.Path);
        Assert.Equal(MuscleTrainingStatus.Primary, resolved.Status);
        Assert.Equal("quads", document.RegionAtPoint(new PointF(50, 50), includeDeep: false));
    }

    [Fact]
    public void No_record_status_stays_neutral_on_the_same_geometry()
    {
        var document = MuscleSvgDocument.Parse(CurvedFixture, ["quads"]);
        var resolved = MuscleBodyDiagram.ResolveShapeStatuses(document, Report("quads")).Single();

        Assert.Same(document.Shapes.Single(shape => shape.RegionId == "quads").Path, resolved.Path);
        Assert.Equal(MuscleTrainingStatus.NoRecord, resolved.Status);
        Assert.Equal(Color.FromArgb("#626B72"), MuscleBodyDiagram.StatusColor(resolved.Status));
    }

    [Fact]
    public void Rendering_records_curved_fills_outlines_and_clipped_detail_commands()
    {
        var document = MuscleSvgDocument.Parse(CurvedFixture, ["quads"]);
        var withoutDetail = MuscleSvgDocument.Parse(
            CurvedFixture.Replace("<path id=\"fiber\" data-detail-for=\"quads\" data-side=\"front\"\n        d=\"M0 50 Q50 25 100 50\" />", ""),
            ["quads"]);
        using var canvas = new PictureCanvas(0, 0, 200, 200);
        using var plainCanvas = new PictureCanvas(0, 0, 200, 200);

        new MuscleBodyDiagram(Report("quads", primarySets: 3), document).Draw(canvas, new RectF(0, 0, 200, 200));
        new MuscleBodyDiagram(Report("quads", primarySets: 3), withoutDetail).Draw(plainCanvas, new RectF(0, 0, 200, 200));

        Assert.True(CommandCount(canvas.Picture) > CommandCount(plainCanvas.Picture));
    }

    [Fact]
    public void Focused_hamstrings_crop_keeps_the_back_thigh_without_both_full_figures()
    {
        var document = BundledDocument();
        var overview = MuscleBodyDiagram.SourceBounds(document, null, null);
        var focus = MuscleBodyDiagram.SourceBounds(document, BodyPart.Legs, "hamstrings");

        Assert.True(focus.Left > 720, "Focused hamstrings should exclude the front figure");
        Assert.True(focus.Width < overview.Width / 2);
        Assert.True(focus.Height < overview.Height / 2);
    }

    [Fact]
    public void Focused_hit_testing_uses_the_same_svg_transform_as_rendering()
    {
        var document = MuscleSvgDocument.Parse(CurvedFixture, ["quads"]);
        var bounds = new RectF(0, 0, 360, 190);
        var renderedPoint = MuscleBodyDiagram.SvgToView(document, new PointF(50, 50), bounds, BodyPart.Legs, "quads");

        Assert.Equal("quads", MuscleBodyDiagram.HitTest(document, renderedPoint, bounds, BodyPart.Legs, "quads"));
        Assert.Null(MuscleBodyDiagram.HitTest(document, new PointF(4, 4), bounds, BodyPart.Legs, "quads"));
    }

    private static MuscleCoverageReport Report(string id, int primarySets = 0, int secondarySets = 0)
    {
        var region = MuscleCatalog.Regions.Single(region => region.Id == id);
        return new MuscleCoverageReport(
            new DateOnly(2026, 9, 7),
            new DateOnly(2026, 9, 13),
            "UTC",
            MuscleCatalog.Version,
            primarySets,
            secondarySets,
            0,
            [new MuscleRegionCoverage(id, region.BodyPart, region.ThaiName, region.EnglishName, primarySets, secondarySets)]);
    }

    private static MuscleSvgDocument BundledDocument() => MuscleSvgDocument.Parse(
        File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TrackZ.Mobile", "Resources", "Raw", "muscles", "body.svg")),
        MuscleCatalog.Regions.Select(region => region.Id));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static int CommandCount(IPicture picture)
    {
        var commands = typeof(StandardPicture).GetField("_commands", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(picture) as Array;
        return commands?.Length ?? throw new InvalidOperationException("Could not inspect recorded drawing commands.");
    }
}
