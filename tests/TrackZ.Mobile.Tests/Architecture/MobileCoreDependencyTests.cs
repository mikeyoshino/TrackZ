using System.Xml.Linq;

namespace TrackZ.Mobile.Tests.Architecture;

public sealed class MobileCoreDependencyTests
{
    [Fact]
    public void Mobile_core_references_contracts_without_maui_dependencies()
    {
        var solutionDirectory = FindSolutionDirectory();
        var project = XDocument.Load(Path.Combine(
            solutionDirectory,
            "src",
            "TrackZ.Mobile.Core",
            "TrackZ.Mobile.Core.csproj"));
        var projectReferences = project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .ToArray();

        Assert.Contains("..\\TrackZ.Contracts\\TrackZ.Contracts.csproj", projectReferences);
        Assert.DoesNotContain(projectReferences, reference => reference?.Contains("Maui", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Mobile_core_owns_nonvisual_sources_without_linking_them_from_the_maui_project()
    {
        var solutionDirectory = FindSolutionDirectory();
        var coreDirectory = Path.Combine(solutionDirectory, "src", "TrackZ.Mobile.Core");
        var project = XDocument.Load(Path.Combine(coreDirectory, "TrackZ.Mobile.Core.csproj"));

        Assert.DoesNotContain(project.Descendants("Compile"), item =>
            item.Attribute("Include")?.Value.Contains("..\\TrackZ.Mobile", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(File.Exists(Path.Combine(coreDirectory, "Features", "Workout", "SetLoggerViewModel.cs")));
        Assert.True(File.Exists(Path.Combine(coreDirectory, "Features", "Workout", "WorkoutViewModel.cs")));
        Assert.True(File.Exists(Path.Combine(coreDirectory, "Sync", "SyncCoordinator.cs")));
    }

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TrackZ.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("TrackZ.slnx was not found from the test output directory.");
    }
}
