using System.Xml.Linq;
using Xunit;

namespace AgentController.Architecture.Tests;

public sealed class MicroDriverOwnershipRulesTests
{
    private const string DriverInterfaceGuid =
        "E2A7CB54-8420-4D51-9DD8-D6575B9251D1";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Theory]
    [InlineData("DeviceIoControl(")]
    [InlineData("IoctlSubmitInput")]
    [InlineData(DriverInterfaceGuid)]
    public void OnlyMicroBrokerOwnsThePrivateDriverContract(string marker)
    {
        var owners = RuntimeSourceFiles()
            .Where(path => File.ReadAllText(path).Contains(
                marker,
                StringComparison.OrdinalIgnoreCase))
            .Select(RelativePath)
            .ToArray();

        Assert.Equal(
            ["src/AgentController.MicroBroker/VhfDriverEndpoint.cs"],
            owners);
    }

    [Theory]
    [InlineData("app/AgentController.csproj")]
    [InlineData(
        "virtual-micro/src/CodexMicro.Desktop/" +
        "CodexMicro.Desktop.csproj")]
    public void DesktopClientsReferenceTheSharedBroker(string projectPath)
    {
        var document = XDocument.Load(Resolve(projectPath));
        var references = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value))
            .ToArray();

        Assert.Contains("AgentController.MicroBroker", references);
    }

    private static IEnumerable<string> RuntimeSourceFiles()
    {
        foreach (var root in new[] { "app", "src", "virtual-micro/src" })
        {
            foreach (var path in Directory.EnumerateFiles(
                         Resolve(root),
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                if (!path.Contains(
                        $"{Path.DirectorySeparatorChar}obj" +
                        Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return path;
                }
            }
        }
    }

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

    private static string Resolve(string relativePath) =>
        Path.GetFullPath(relativePath, RepositoryRoot);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "AgentController.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from the test output directory.");
    }
}
