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
