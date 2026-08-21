using CodexMicro.Desktop.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class DeepSeekSetupCoordinatorTests
{
    [Fact]
    public void ManagedLaunchUsesDedicatedDistributionAndSelectedPort()
    {
        var arguments = DeepSeekSetupCoordinator.BuildManagedLaunchArguments(3091);

        Assert.Contains("--distribution CodexMicro-DeepSeek", arguments);
        Assert.Contains("--user codexmicro", arguments);
        Assert.Contains(
            "/home/codexmicro/.local/share/codex-micro/deepseek/bin/start-dsh-wsl.sh",
            arguments);
        Assert.EndsWith("--port 3091", arguments, StringComparison.Ordinal);
        Assert.Equal(
            "http://127.0.0.1:3091/__agentcontroller/micro/request",
            DeepSeekSetupCoordinator.BuildControlUri(3091));
    }

    [Fact]
    public void ParsesWslUtf16LikeOutputWithoutAssumingUbuntuName()
    {
        var names = DeepSeekSetupCoordinator.ParseDistributionNames(
            "C\0o\0d\0e\0x\0M\0i\0c\0r\0o\0-\0D\0e\0e\0p\0S\0e\0e\0k\0\r\0\n\0" +
            "M\0y\0-\0D\0i\0s\0t\0r\0o\0\r\0\n\0");

        Assert.Equal(
            ["CodexMicro-DeepSeek", "My-Distro"],
            names);
    }

    [Fact]
    public void ProcessTextNormalizationRemovesWslNullCharacters()
    {
        Assert.Equal(
            "Ubuntu-24.04",
            DeepSeekProcessRunner.NormalizeProcessText(
                "U\0b\0u\0n\0t\0u\0-\02\04\0.\00\04\0\r\0\n\0"));
    }

    [Fact]
    public void ParsesPinnedRcVersionAndInstallerMarkers()
    {
        Assert.Equal(
            "0.1.0-rc.8",
            DeepSeekSetupCoordinator.ParseExpectedDshVersion(
                "# release\nCODEX_MICRO_DSH_VERSION=0.1.0-rc.8\n"));
        Assert.Null(DeepSeekSetupCoordinator.ParseExpectedDshVersion(
            "CODEX_MICRO_DSH_VERSION=../../unsafe\n"));

        var markers = DeepSeekSetupCoordinator.ParseInstallerMarkers(
            "noise\nactual-dsh=0.1.0-rc.6\nupgrade-pending=1\n" +
            "upgrade-backup=/home/codexmicro/deepseek.backup-rc6\n");

        Assert.Equal("0.1.0-rc.6", markers["actual-dsh"]);
        Assert.Equal("1", markers["upgrade-pending"]);
        Assert.Equal(
            "/home/codexmicro/deepseek.backup-rc6",
            markers["upgrade-backup"]);
    }

    [Fact]
    public async Task UserManagedHarnessNeverRunsManagedVersionInspection()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var runner = new RuntimeInspectionRunner("0.1.0-rc.6");
            var registry = CreateRegistry(
                directory,
                new MicroHarnessConnectionSettings(
                    "deepseek-harness-micro-v1",
                    Executable: null,
                    Arguments: null,
                    WorkingDirectory: null,
                    AutoStart: false,
                    ControlUri: DeepSeekSetupCoordinator.BuildControlUri(3080)));
            var coordinator = CreateCoordinator(registry, runner, directory);

            var status = await coordinator.InspectManagedRuntimeAsync(
                registry.Resolve("deepseek-harness"));

            Assert.Equal(DeepSeekManagedRuntimeState.NotManaged, status.State);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("0.1.0-rc.8", false, "Current")]
    [InlineData("0.1.0-rc.6", false, "UpgradeRequired")]
    [InlineData("0.1.0-rc.8", true, "UpgradePending")]
    public async Task InspectsActualManagedPackageVersionEvenAfterSetup(
        string actualVersion,
        bool pending,
        string expectedState)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var runner = new RuntimeInspectionRunner(actualVersion, pending);
            var registry = CreateRegistry(directory, ManagedConnection(3094));
            Assert.True(registry.MarkSetupCompleted("deepseek-harness"));
            var coordinator = CreateCoordinator(registry, runner, directory);

            var status = await coordinator.InspectManagedRuntimeAsync(
                registry.Resolve("deepseek-harness"));

            Assert.Equal(expectedState, status.State.ToString());
            Assert.Equal("0.1.0-rc.8", status.ExpectedVersion);
            Assert.Equal(actualVersion, status.ActualVersion);
            Assert.Equal(pending, status.BackupPath is not null);
            Assert.Equal(3, runner.Calls.Count);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.Contains("--runtime-status"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ManagedUpgradeStopsInstallsHealthChecksAndCommits()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var port = ReservePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var responseTask = RespondWithReadyAdapterAsync(listener);
            var runner = new RuntimeUpgradeRunner();
            var registry = CreateRegistry(directory, ManagedConnection(port));
            Assert.True(registry.MarkSetupCompleted("deepseek-harness"));
            var coordinator = CreateCoordinator(registry, runner, directory);

            var result = await coordinator.UpgradeManagedRuntimeAsync();

            await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.Success, result.Message);
            Assert.Equal(8, result.Step);
            Assert.Contains("0.1.0-rc.8", result.Message);
            Assert.Contains("deepseek.backup-rc6", result.Message);
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.SequenceEqual(
                    ["--terminate", DeepSeekSetupCoordinator.ManagedDistributionName]));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.Contains("CODEX_MICRO_DSH_OFFLINE=0") &&
                    call.Arguments.Contains("bash") &&
                    !call.Arguments.Contains("--runtime-status"));
            Assert.Contains(
                runner.Calls,
                call => call.Arguments.Contains("--commit-upgrade"));
            var configured = registry.Resolve("deepseek-harness");
            Assert.Equal(
                DeepSeekSetupCoordinator.BuildControlUri(port),
                configured.ControlUri);
            Assert.Equal(
                DeepSeekSetupCoordinator.BuildManagedLaunchArguments(port),
                configured.Connection.Arguments);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ManagedScriptsContainNoDeveloperDriveAssumptions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var scripts = new[]
        {
            Path.Combine(
                repositoryRoot,
                "micro-bridge",
                "DeepSeekHarness",
                "scripts",
                "install-dsh-wsl-runtime.sh"),
            Path.Combine(
                repositoryRoot,
                "micro-bridge",
                "DeepSeekHarness",
                "scripts",
                "start-dsh-wsl.sh"),
        };

        foreach (var path in scripts)
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("/mnt/d/AgentController", text);
            Assert.DoesNotContain("/mnt/d/project", text);
            Assert.DoesNotContain("D:\\AgentController", text);
            Assert.DoesNotContain("DSH_SOURCE_CHECKOUT", text);
        }
    }

    [Fact]
    public async Task ManagedSetupPersistsOnlyAfterBridgeHealthCheck()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pluginRoot = Path.Combine(
            repositoryRoot,
            "micro-bridge",
            "DeepSeekHarness");
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-managed-setup-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var initialPort = ReservePort();
            var officialPort = ReservePort();
            var managedPort = ReservePort();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{managedPort}/");
            listener.Start();
            var responseTask = RespondWithReadyAdapterAsync(listener);
            var registry = new MicroHarnessRegistry(
                definitions:
                [
                    new(
                        "deepseek-harness",
                        "DeepSeek Harness",
                        "test",
                        null,
                        true,
                        new(
                            "deepseek-harness-micro-v1",
                            null,
                            null,
                            null,
                            false,
                            120_000,
                            $"http://127.0.0.1:{initialPort}/__agentcontroller/micro/request")),
                ],
                settingsPath: Path.Combine(directory, "harness-settings.json"));
            var runner = new FakeProcessRunner();
            var coordinator = new DeepSeekSetupCoordinator(
                registry,
                runner,
                directory,
                pluginRoot,
                () => managedPort,
                new Uri(
                    $"http://127.0.0.1:{officialPort}/__agentcontroller/micro/request"));
            var observed = new List<DeepSeekSetupProgress>();

            var result = await coordinator.ConfigureManagedAsync(
                new CallbackProgress<DeepSeekSetupProgress>(observed.Add));

            await responseTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(result.Success, result.Message);
            Assert.Equal(8, result.Step);
            Assert.True(registry.IsSetupCompleted("deepseek-harness"));
            var configured = registry.Resolve("deepseek-harness");
            Assert.True(configured.Connection.AutoStart);
            Assert.Equal(
                DeepSeekSetupCoordinator.BuildControlUri(managedPort),
                configured.ControlUri);
            Assert.Equal(
                300_000,
                configured.Connection.ReadyTimeoutMilliseconds);
            Assert.Contains($"--port {managedPort}", configured.Connection.Arguments);
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8],
                observed.Select(item => item.Step).Distinct());
            Assert.Equal(4, runner.Calls.Count);
            Assert.All(runner.Calls, call => Assert.False(call.Elevated));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-managed-version-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static MicroHarnessConnectionSettings ManagedConnection(int port) =>
        new(
            "deepseek-harness-micro-v1",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "wsl.exe"),
            DeepSeekSetupCoordinator.BuildManagedLaunchArguments(port),
            WorkingDirectory: null,
            AutoStart: true,
            ReadyTimeoutMilliseconds: 300_000,
            ControlUri: DeepSeekSetupCoordinator.BuildControlUri(port));

    private static MicroHarnessRegistry CreateRegistry(
        string directory,
        MicroHarnessConnectionSettings connection) =>
        new(
            definitions:
            [
                new(
                    "deepseek-harness",
                    "DeepSeek Harness",
                    "test",
                    null,
                    true,
                    connection),
            ],
            settingsPath: Path.Combine(directory, "harness-settings.json"));

    private static DeepSeekSetupCoordinator CreateCoordinator(
        MicroHarnessRegistry registry,
        IDeepSeekProcessRunner runner,
        string directory)
    {
        var repositoryRoot = FindRepositoryRoot();
        return new(
            registry,
            runner,
            directory,
            Path.Combine(
                repositoryRoot,
                "micro-bridge",
                "DeepSeekHarness"),
            () => 3080,
            new Uri(DeepSeekSetupCoordinator.BuildControlUri(3080)),
            Path.Combine(directory, "wsl.exe"));
    }

    private static async Task RespondWithReadyAdapterAsync(HttpListener listener)
    {
        var context = await listener.GetContextAsync();
        const string body =
            "{\"success\":true,\"message\":\"ready\",\"status\":\"foreground\"}";
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private sealed class FakeProcessRunner : IDeepSeekProcessRunner
    {
        internal List<(
            string Executable,
            IReadOnlyList<string> Arguments,
            bool Elevated)> Calls { get; } = [];

        public Task<DeepSeekProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            bool elevated,
            CancellationToken cancellationToken)
        {
            Calls.Add((executable, arguments, elevated));
            if (arguments.Contains("--list"))
            {
                return Task.FromResult(new DeepSeekProcessResult(
                    0,
                    "CodexMicro-DeepSeek\r\n",
                    string.Empty));
            }
            if (arguments.Contains("wslpath"))
            {
                return Task.FromResult(new DeepSeekProcessResult(
                    0,
                    "/mnt/c/package/install-dsh-wsl-runtime.sh",
                    string.Empty));
            }
            if (arguments.Contains("bash"))
            {
                return Task.FromResult(new DeepSeekProcessResult(
                    0,
                    "managed-ready=1",
                    string.Empty));
            }

            throw new InvalidOperationException(
                $"Unexpected process call: {string.Join(' ', arguments)}");
        }
    }

    private sealed class RuntimeInspectionRunner(
        string actualVersion,
        bool pending = false) : IDeepSeekProcessRunner
    {
        internal List<(
            string Executable,
            IReadOnlyList<string> Arguments,
            bool Elevated)> Calls { get; } = [];

        public Task<DeepSeekProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            bool elevated,
            CancellationToken cancellationToken)
        {
            Calls.Add((executable, arguments, elevated));
            if (arguments.Contains("--list"))
            {
                return Result("CodexMicro-DeepSeek\r\n");
            }
            if (arguments.Contains("wslpath"))
            {
                return Result("/mnt/c/package/install-dsh-wsl-runtime.sh");
            }
            if (arguments.Contains("--runtime-status"))
            {
                return Result(
                    $"expected-dsh=0.1.0-rc.8\nactual-dsh={actualVersion}\n" +
                    $"upgrade-pending={(pending ? 1 : 0)}\n" +
                    (pending
                        ? "upgrade-pending-target=0.1.0-rc.8\n" +
                          "upgrade-backup=/home/codexmicro/deepseek.backup-rc6\n"
                        : string.Empty));
            }
            throw new InvalidOperationException(
                $"Unexpected process call: {string.Join(' ', arguments)}");
        }

        private static Task<DeepSeekProcessResult> Result(string output) =>
            Task.FromResult(new DeepSeekProcessResult(
                0,
                output,
                string.Empty));
    }

    private sealed class RuntimeUpgradeRunner : IDeepSeekProcessRunner
    {
        internal List<(
            string Executable,
            IReadOnlyList<string> Arguments,
            bool Elevated)> Calls { get; } = [];

        public Task<DeepSeekProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            bool elevated,
            CancellationToken cancellationToken)
        {
            Calls.Add((executable, arguments, elevated));
            if (arguments.Contains("--list"))
            {
                return Result("CodexMicro-DeepSeek\r\n");
            }
            if (arguments.Contains("wslpath"))
            {
                return Result("/mnt/c/package/install-dsh-wsl-runtime.sh");
            }
            if (arguments.Contains("--runtime-status"))
            {
                return Result(
                    "expected-dsh=0.1.0-rc.8\n" +
                    "actual-dsh=0.1.0-rc.6\n" +
                    "upgrade-pending=0\n");
            }
            if (arguments.Contains("--terminate"))
            {
                return Result(string.Empty);
            }
            if (arguments.Contains("--commit-upgrade"))
            {
                return Result(
                    "upgrade-commit=ready\n" +
                    "upgrade-backup=/home/codexmicro/deepseek.backup-rc6\n");
            }
            if (arguments.Contains("bash"))
            {
                return Result(
                    "upgrade-prepared=1\n" +
                    "upgrade-from=0.1.0-rc.6\n" +
                    "upgrade-to=0.1.0-rc.8\n" +
                    "upgrade-backup=/home/codexmicro/deepseek.backup-rc6\n" +
                    "managed-ready=1\n");
            }
            throw new InvalidOperationException(
                $"Unexpected process call: {string.Join(' ', arguments)}");
        }

        private static Task<DeepSeekProcessResult> Result(string output) =>
            Task.FromResult(new DeepSeekProcessResult(
                0,
                output,
                string.Empty));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentController.sln")) ||
                Directory.Exists(Path.Combine(directory.FullName, "micro-bridge")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("AgentController repository root was not found.");
    }
}
