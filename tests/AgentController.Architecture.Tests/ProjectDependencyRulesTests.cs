using System.Xml.Linq;
using Xunit;

namespace AgentController.Architecture.Tests;

public sealed class ProjectDependencyRulesTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string, string[]> AllowedProjectReferences => new()
    {
        {
            "src/AgentController.Domain/AgentController.Domain.csproj",
            []
        },
        {
            "src/AgentController.Platform.Abstractions/AgentController.Platform.Abstractions.csproj",
            ["AgentController.Domain"]
        },
        {
            "src/AgentController.Application/AgentController.Application.csproj",
            ["AgentController.Domain", "AgentController.Platform.Abstractions"]
        },
        {
            "src/AgentController.Platform.MacOS/AgentController.Platform.MacOS.csproj",
            ["AgentController.Platform.Abstractions"]
        },
        {
            "src/AgentController.Desktop/AgentController.Desktop.csproj",
            ["AgentController.Application", "AgentController.Platform.MacOS"]
        },
    };

    [Theory]
    [MemberData(nameof(AllowedProjectReferences))]
    public void CoreProjectReferencesStayInsideTheAllowedGraph(
        string relativeProjectPath,
        string[] allowedReferences)
    {
        var projectPath = Resolve(relativeProjectPath);
        var actualReferences = ReadProjectReferences(projectPath);

        Assert.Equal(
            allowedReferences.Order(StringComparer.Ordinal),
            actualReferences.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("src/AgentController.Domain/AgentController.Domain.csproj")]
    [InlineData("src/AgentController.Application/AgentController.Application.csproj")]
    [InlineData("src/AgentController.Platform.Abstractions/AgentController.Platform.Abstractions.csproj")]
    [InlineData("src/AgentController.Platform.MacOS/AgentController.Platform.MacOS.csproj")]
    [InlineData("src/AgentController.Desktop/AgentController.Desktop.csproj")]
    public void CoreProjectsRemainCrossPlatform(string relativeProjectPath)
    {
        var document = XDocument.Load(Resolve(relativeProjectPath));
        var targetFramework = document
            .Descendants("TargetFramework")
            .Select(element => element.Value.Trim())
            .Single();

        Assert.Equal("net10.0", targetFramework);
        Assert.Empty(document.Descendants("UseWPF"));
        Assert.Empty(document.Descendants("UseWindowsForms"));
    }

    [Fact]
    public void DomainHasNoPackageReferences()
    {
        var document = XDocument.Load(Resolve(
            "src/AgentController.Domain/AgentController.Domain.csproj"));

        Assert.Empty(document.Descendants("PackageReference"));
    }

    private static IReadOnlyCollection<string> ReadProjectReferences(
        string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException(
                $"Project path has no directory: {projectPath}");
        var document = XDocument.Load(projectPath);

        return document
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(include!, projectDirectory))
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .ToArray();
    }

    private static string Resolve(string relativePath) =>
        Path.GetFullPath(relativePath, RepositoryRoot);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AgentController.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test output directory.");
    }
}
