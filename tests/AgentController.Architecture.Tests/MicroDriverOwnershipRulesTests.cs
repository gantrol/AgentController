using System.Text.RegularExpressions;
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
        "virtual-micro/src/AgentController.MicroSurface.Wpf/" +
        "AgentController.MicroSurface.Wpf.csproj")]
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

    [Fact]
    public void MicroSurfaceIsHostedWithoutAProductVersionGate()
    {
        Assert.False(File.Exists(Resolve(
            "virtual-micro/src/CodexMicro.Desktop/" +
            "CodexMicro.Desktop.csproj")));
        Assert.False(File.Exists(Resolve(
            "virtual-micro/src/CodexMicro.Desktop/App.xaml.cs")));

        var project = XDocument.Load(Resolve(
            "virtual-micro/src/AgentController.MicroSurface.Wpf/" +
            "AgentController.MicroSurface.Wpf.csproj"));
        Assert.DoesNotContain(
            project.Descendants("OutputType"),
            element => string.Equals(
                element.Value,
                "WinExe",
                StringComparison.OrdinalIgnoreCase));

        var runtimeSource = string.Join(
            '\n',
            Directory.EnumerateFiles(
                    Resolve("virtual-micro/src/CodexMicro.Desktop"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        Assert.DoesNotContain(
            "FileVersionInfo.GetVersionInfo",
            runtimeSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "CodexCompatibilityProbe",
            runtimeSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PhysicalRightStickIsMicroOnlyAndCannotFallBackToUia()
    {
        var mainWindow = File.ReadAllText(Resolve(
            "app/MainWindow.xaml.cs"));

        Assert.Contains(
            "_microInput.SendEncoderSteps(steps)",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "_microInput.SendEncoderPress()",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_composerAutomation.DialStep(",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_composerAutomation.DialPress(",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_composerAutomation.DialSelect(",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VirtualMicroHidIdentityAlwaysAdvertisesWiredUsb()
    {
        var header = File.ReadAllText(Resolve(
            "virtual-micro/driver/CodexMicroVhfUm/Driver.h"));
        var driver = File.ReadAllText(Resolve(
            "virtual-micro/driver/CodexMicroVhfUm/Driver.c"));
        var release = Regex.Match(
            header,
            @"#define\s+VMICRO_USB_RELEASE_NUMBER\s+0x([0-9A-Fa-f]+)U?");

        Assert.True(release.Success);
        var releaseNumber = Convert.ToUInt32(
            release.Groups[1].Value,
            16);
        Assert.Equal(0U, releaseNumber & 0x0003U);
        Assert.Equal(
            2,
            Regex.Matches(
                driver,
                @"VersionNumber\s*=\s*VMICRO_USB_RELEASE_NUMBER")
                .Count);
        Assert.DoesNotMatch(
            @"VersionNumber\s*=\s*0x[0-9A-Fa-f]+",
            driver);
    }

    [Fact]
    public void VirtualMicroHidIdentityIsStableAndVidPidFirstForHotPlug()
    {
        var driver = File.ReadAllText(Resolve(
            "virtual-micro/driver/CodexMicroVhfUm/Driver.c"));

        var vendorSpecificId = driver.IndexOf(
            "L\"VHF\\\\VID_303A&PID_8360\\0\"",
            StringComparison.Ordinal);
        var vendorClassId = driver.IndexOf(
            "L\"HID_DEVICE_SYSTEM_VHF\\0\"",
            vendorSpecificId,
            StringComparison.Ordinal);
        var keyboardSpecificId = driver.IndexOf(
            "L\"VHF\\\\VID_303A&PID_8361\\0\"",
            StringComparison.Ordinal);
        var keyboardClassId = driver.IndexOf(
            "L\"HID_DEVICE_SYSTEM_VHF\\0\"",
            keyboardSpecificId,
            StringComparison.Ordinal);

        Assert.True(vendorSpecificId >= 0);
        Assert.True(vendorClassId > vendorSpecificId);
        Assert.True(keyboardSpecificId >= 0);
        Assert.True(keyboardClassId > keyboardSpecificId);
        Assert.Equal(
            2,
            Regex.Matches(driver, @"InstanceIDLength\s*=").Count);
        Assert.Equal(
            2,
            Regex.Matches(driver, @"HardwareIDsLength\s*=").Count);
        Assert.Equal(
            2,
            Regex.Matches(driver, @"HardwareIDs\s*=\s*[A-Za-z]").Count);
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
