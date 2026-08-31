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
        "virtual-micro/src/CodexMicro.DesktopHost/" +
        "CodexMicro.DesktopHost.csproj")]
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
    public void MicroKeypadIsIndependentWpfHostAndNotHostedByMainApp()
    {
        var hostProject = XDocument.Load(Resolve(
            "virtual-micro/src/CodexMicro.DesktopHost/" +
            "CodexMicro.DesktopHost.csproj"));
        Assert.Equal(
            "WinExe",
            hostProject.Descendants("OutputType").Single().Value);
        Assert.Equal(
            "true",
            hostProject.Descendants("UseWPF").Single().Value);
        Assert.Equal(
            "false",
            hostProject.Descendants("SelfContained").Single().Value);
        Assert.Equal(
            "true",
            hostProject.Descendants("PublishSingleFile").Single().Value);

        var mainProject = XDocument.Load(Resolve(
            "app/AgentController.csproj"));
        var mainReferences = mainProject
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? "")
            .ToArray();
        Assert.DoesNotContain(
            mainReferences,
            value => value.Contains(
                "MicroSurface.Wpf",
                StringComparison.OrdinalIgnoreCase));
        var mainWindowSource = File.ReadAllText(Resolve(
            "app/MainWindow.xaml.cs"));
        var mainWindowXaml = File.ReadAllText(Resolve(
            "app/MainWindow.xaml"));
        Assert.DoesNotContain(
            "MicroKeypadLauncher",
            mainWindowSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "MicroKeypadButton",
            mainWindowXaml,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Resolve(
            "app/Services/Micro/MicroKeypadLauncher.cs")));

        var xaml = XDocument.Load(Resolve(
            "virtual-micro/src/CodexMicro.Desktop/MainWindow.xaml"));
        var window = xaml.Root!;
        Assert.Equal("None", window.Attribute("WindowStyle")?.Value);
        Assert.Equal("NoResize", window.Attribute("ResizeMode")?.Value);
        Assert.Equal("True", window.Attribute("AllowsTransparency")?.Value);
        Assert.Equal("Transparent", window.Attribute("Background")?.Value);

        var hostSource = File.ReadAllText(Resolve(
            "virtual-micro/src/CodexMicro.DesktopHost/App.xaml.cs"));
        Assert.Contains("MicroTrayIcon", hostSource, StringComparison.Ordinal);
        Assert.Contains(
            "MicroBrokerHost.IsBrokerArgument",
            hostSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "_surface.StartBackgroundServices()",
            hostSource,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MicroKeypadTrayCanToggleAndExit()
    {
        var traySource = File.ReadAllText(Resolve(
            "virtual-micro/src/CodexMicro.DesktopHost/MicroTrayIcon.cs"));

        Assert.Contains("\"收起小键盘\"", traySource, StringComparison.Ordinal);
        Assert.Contains("\"显示小键盘\"", traySource, StringComparison.Ordinal);
        Assert.Contains("\"退出\"", traySource, StringComparison.Ordinal);
        Assert.Contains("\"Hide keypad\"", traySource, StringComparison.Ordinal);
        Assert.Contains("\"Show keypad\"", traySource, StringComparison.Ordinal);
        Assert.Contains(
            "\"Start with Windows\"",
            traySource,
            StringComparison.Ordinal);
        Assert.Contains("MicroLanguage.Auto", traySource, StringComparison.Ordinal);
        Assert.Contains(
            "_notifyIcon.DoubleClick",
            traySource,
            StringComparison.Ordinal);

        var startupSource = File.ReadAllText(Resolve(
            "virtual-micro/src/CodexMicro.DesktopHost/" +
            "MicroStartupRegistration.cs"));
        Assert.Contains(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            startupSource,
            StringComparison.Ordinal);
        Assert.Contains("--background", startupSource, StringComparison.Ordinal);

        var project = XDocument.Load(Resolve(
            "virtual-micro/src/CodexMicro.DesktopHost/" +
            "CodexMicro.DesktopHost.csproj"));
        Assert.EndsWith(
            @"Assets\CodexMicro.ico",
            project.Descendants("ApplicationIcon").Single().Value,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PhysicalRightStickKeepsCodexHidAndDeepSeekRoutesIsolated()
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
        Assert.Contains(
            "? _composerAutomation.DialStep(",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "? _composerAutomation.DialPress(",
            mainWindow,
            StringComparison.Ordinal);

        var selection = File.ReadAllText(Resolve(
            "app/Agents/AgentTargetSelection.cs"));
        Assert.Contains(
            "_selection.Active.Id == _targetId",
            selection,
            StringComparison.Ordinal);
        Assert.Contains(
            "agent.not-selected",
            selection,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_composerAutomation.DialSelect(",
            mainWindow,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VirtualMicroCodexDialUsesNativeInputAndSemanticModelBridge()
    {
        var mainWindow = File.ReadAllText(Resolve(
            "virtual-micro/src/CodexMicro.Desktop/MainWindow.xaml.cs"));
        var broker = File.ReadAllText(Resolve(
            "virtual-micro/src/CodexMicro.Desktop/Services/" +
            "VirtualMicroBroker.cs"));
        var modelBridge = File.ReadAllText(Resolve(
            "virtual-micro/src/CodexMicro.Desktop/Services/" +
            "CodexModelToggleService.cs"));

        Assert.Contains(
            "_broker.StepEncoderAsync(reportedClockwise)",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "_broker.TapKeyAsync(\"ENC\")",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "ToggleQuickModelAsync",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TapDialogKeyAsync",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "_cachedDialSelection",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TapDialogKeyAsync",
            broker,
            StringComparison.Ordinal);
        Assert.Contains("codex-ipc", modelBridge, StringComparison.Ordinal);
        Assert.Contains(
            "thread-follower-update-thread-settings",
            modelBridge,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "System.Windows.Automation",
            modelBridge,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BaseViewSwitchesAgentBeforeBridgeAndForegroundGates()
    {
        var mainWindow = File.ReadAllText(Resolve(
            "app/MainWindow.xaml.cs"));
        var viewEdge = mainWindow.IndexOf(
            ".HasFlag(ControllerButtons.Back)",
            StringComparison.Ordinal);
        var switchCall = mainWindow.IndexOf(
            "SwitchActiveAgent();",
            viewEdge,
            StringComparison.Ordinal);
        var bridgeGate = mainWindow.IndexOf(
            "if (!_settings.BridgeEnabled)",
            switchCall,
            StringComparison.Ordinal);

        Assert.True(viewEdge >= 0, "The base View edge is missing.");
        Assert.True(
            switchCall > viewEdge,
            "The base View edge must switch the selected Agent.");
        Assert.True(
            bridgeGate > switchCall,
            "Agent switching must remain available before the bridge gate.");
        Assert.Contains(
            "_settings.ActiveAgentId = _activeAgent.Id.Value",
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
