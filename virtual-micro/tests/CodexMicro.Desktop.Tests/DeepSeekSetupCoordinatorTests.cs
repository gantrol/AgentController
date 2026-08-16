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
            Assert.Contains($"--port {managedPort}", configured.Connection.Arguments);
            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8],
                observed.Select(item => item.Step).Distinct());
            Assert.Equal(3, runner.Calls.Count);
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
