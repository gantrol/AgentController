using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record DeepSeekEndpointProbe(
    Uri BaseUri,
    Uri ControlUri,
    bool WebReachable,
    bool BridgeReachable,
    string Message);

internal sealed record DeepSeekSetupProgress(
    int Step,
    int TotalSteps,
    string Title,
    string Message);

internal enum DeepSeekSetupDisposition
{
    ManagedReady,
    RestartRequired,
    Cancelled,
    Failed,
}

internal sealed record DeepSeekSetupResult(
    DeepSeekSetupDisposition Disposition,
    string Message,
    int Step,
    int TotalSteps = DeepSeekSetupCoordinator.TotalSetupSteps)
{
    internal bool Success => Disposition == DeepSeekSetupDisposition.ManagedReady;
}

internal sealed record DeepSeekProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface IDeepSeekProcessRunner
{
    Task<DeepSeekProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        bool elevated,
        CancellationToken cancellationToken);
}

internal sealed class DeepSeekProcessRunner : IDeepSeekProcessRunner
{
    public async Task<DeepSeekProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        bool elevated,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = elevated,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !elevated,
            RedirectStandardError = !elevated,
        };
        if (elevated)
        {
            startInfo.Verb = "runas";
        }
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new(-1, string.Empty, "The process could not be started.");
            }

            Task<string>? output = null;
            Task<string>? error = null;
            if (!elevated)
            {
                output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                error = process.StandardError.ReadToEndAsync(cancellationToken);
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                        Win32Exception or
                        NotSupportedException)
                {
                    // Cancellation still belongs to the caller. An elevated
                    // child may already have exited or be outside our job.
                }
                throw;
            }

            return new(
                process.ExitCode,
                NormalizeProcessText(output is null ? null : await output),
                NormalizeProcessText(error is null ? null : await error));
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(1223, string.Empty, "Windows elevation was cancelled.");
        }
        catch (Win32Exception exception)
        {
            return new(exception.NativeErrorCode, string.Empty, exception.Message);
        }
    }

    internal static string NormalizeProcessText(string? value) =>
        (value ?? string.Empty).Replace("\0", string.Empty).Trim();
}

/// <summary>
/// Owns the explicit DeepSeek first-run path. Existing Harness installations
/// are only probed; managed setup writes an isolated launch configuration after
/// the user chooses it and the app-owned WSL runtime has been provisioned.
/// </summary>
internal sealed class DeepSeekSetupCoordinator
{
    internal const int TotalSetupSteps = 8;
    internal const int OfficialDefaultPort = 3080;
    internal const string ManagedDistributionName = "CodexMicro-DeepSeek";
    internal const string ManagedDistributionSource = "Ubuntu-24.04";
    internal const string ManagedLinuxUser = "codexmicro";
    internal const string ManagedStartPath =
        "/home/codexmicro/.local/share/codex-micro/deepseek/bin/start-dsh-wsl.sh";

    private static readonly HttpClient ProbeClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromMilliseconds(900),
        })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly MicroHarnessRegistry _registry;
    private readonly IDeepSeekProcessRunner _processRunner;
    private readonly string _localAppData;
    private readonly string? _pluginRootOverride;
    private readonly Func<int?> _portSelector;
    private readonly Uri _officialControlUri;

    internal DeepSeekSetupCoordinator(
        MicroHarnessRegistry registry,
        IDeepSeekProcessRunner? processRunner = null,
        string? localAppData = null,
        string? pluginRoot = null,
        Func<int?>? portSelector = null,
        Uri? officialControlUri = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _processRunner = processRunner ?? new DeepSeekProcessRunner();
        _localAppData = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _pluginRootOverride = pluginRoot;
        _portSelector = portSelector ?? (() =>
            FindAvailablePort(OfficialDefaultPort));
        _officialControlUri = officialControlUri ??
            new Uri(MicroHarnessRegistry.DeepSeekControlUri);
    }

    internal async Task<DeepSeekEndpointProbe> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        var harness = _registry.Resolve("deepseek-harness");
        var configuredControlUri = Uri.TryCreate(
            harness.ControlUri,
            UriKind.Absolute,
            out var configuredControl)
                ? configuredControl
                : _officialControlUri;
        var candidates = new[] { configuredControlUri, _officialControlUri }
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DeepSeekEndpointProbe? webOnly = null;
        foreach (var controlUri in candidates)
        {
            var baseUri = ToBaseUri(controlUri);
            var bridgeReachable = await ProbeBridgeAsync(
                controlUri,
                cancellationToken);
            var webReachable = bridgeReachable || await ProbeWebAsync(
                baseUri,
                cancellationToken);
            if (bridgeReachable)
            {
                return new(
                    baseUri,
                    controlUri,
                    WebReachable: true,
                    BridgeReachable: true,
                    $"DeepSeek Harness and the Micro bridge are ready at {baseUri}.");
            }
            if (webReachable && webOnly is null)
            {
                webOnly = new(
                    baseUri,
                    controlUri,
                    WebReachable: true,
                    BridgeReachable: false,
                    $"DeepSeek Harness is running at {baseUri}, but the Micro bridge is missing.");
            }
        }

        return webOnly ?? new(
            ToBaseUri(configuredControlUri),
            configuredControlUri,
            WebReachable: false,
            BridgeReachable: false,
            $"No DeepSeek Harness was found at {ToBaseUri(configuredControlUri)}.");
    }

    internal async Task<DeepSeekSetupResult> ConfigureManagedAsync(
        IProgress<DeepSeekSetupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        void Report(int step, string title, string message) =>
            progress?.Report(new(step, TotalSetupSteps, title, message));

        Report(1, "检测已有 Harness", "正在检查已保存地址和官方默认端口 3080。");
        var probe = await ProbeAsync(cancellationToken);
        if (probe.BridgeReachable)
        {
            AdoptProbeEndpoint(probe);
            _registry.MarkSetupCompleted("deepseek-harness");
            return new(
                DeepSeekSetupDisposition.ManagedReady,
                "检测到已经可用的 DeepSeek Harness 与 Micro 桥接，无需安装。",
                1);
        }

        Report(2, "确认配置方式", "将准备隔离的程序托管环境，不修改已有 Harness。");
        var pluginRoot = ResolvePluginRoot();
        if (pluginRoot is null)
        {
            return Failed(
                2,
                "当前安装包不包含 DeepSeek Micro 桥接载荷；可改用“连接已有 Harness”，或安装 DeepSeek 特调包。");
        }

        var wslExecutable = ResolveWslExecutable();
        if (wslExecutable is null)
        {
            return Failed(
                3,
                "Windows 中未找到 wsl.exe。请先安装或修复 Microsoft WSL，然后重试。");
        }

        Report(3, "检查 WSL", "正在检查程序专用的 WSL 发行版。");
        var distribution = await EnsureManagedDistributionAsync(
            wslExecutable,
            pluginRoot,
            cancellationToken);
        if (distribution.Disposition is not null)
        {
            return new(
                distribution.Disposition.Value,
                distribution.Message,
                3);
        }

        Report(4, "验证安装载荷", "正在确定端口并验证自定位安装脚本。");
        var installerPath = Path.Combine(
            pluginRoot,
            "scripts",
            "install-dsh-wsl-runtime.sh");
        if (!File.Exists(installerPath))
        {
            return Failed(4, $"托管安装脚本缺失：{installerPath}");
        }

        var port = _portSelector();
        if (port is null)
        {
            return Failed(4, "没有找到可用的本机回环端口（已检查 3080–3180）。");
        }

        var installerWslPath = await ConvertToWslPathAsync(
            wslExecutable,
            installerPath,
            cancellationToken);
        if (installerWslPath is null)
        {
            return Failed(4, "WSL 无法访问安装包中的 DeepSeek 桥接脚本。");
        }

        Report(5, "准备运行环境", "正在安装固定版本的 Node 与官方 DeepSeek Harness。");
        var install = await _processRunner.RunAsync(
            wslExecutable,
            [
                "--distribution",
                ManagedDistributionName,
                "--user",
                "root",
                "--exec",
                "env",
                $"CODEX_MICRO_DSH_USER={ManagedLinuxUser}",
                "bash",
                installerWslPath,
            ],
            elevated: false,
            cancellationToken);
        if (install.ExitCode != 0)
        {
            var detail = FirstUsefulMessage(
                install.StandardError,
                install.StandardOutput,
                $"WSL 安装器退出代码 {install.ExitCode}。");
            return Failed(5, detail);
        }

        Report(6, "安装桥接插件", "DeepSeek Harness 已安装，正在验证 Micro 桥接 bundle。");
        if (!install.StandardOutput.Contains(
                "managed-ready=1",
                StringComparison.Ordinal))
        {
            return Failed(6, "安装器结束了，但没有返回 Micro 桥接就绪标记。");
        }

        Report(7, "保存并启动", $"正在保存托管启动方式并使用端口 {port.Value} 启动。");
        var connection = new MicroHarnessConnectionSettings(
            "deepseek-harness-micro-v1",
            wslExecutable,
            BuildManagedLaunchArguments(port.Value),
            Path.GetDirectoryName(wslExecutable),
            AutoStart: true,
            ReadyTimeoutMilliseconds:
                MicroHarnessRegistry.DeepSeekReadyTimeoutMilliseconds,
            ControlUri: BuildControlUri(port.Value));
        if (!_registry.UpdateConnectionSettings("deepseek-harness", connection))
        {
            return Failed(7, "托管环境已准备，但启动配置无法保存到本机。");
        }

        var activation = await _registry.ActivateUntilSurfaceReadyAsync(
            "deepseek-harness",
            cancellationToken: cancellationToken);
        Report(8, "健康检查", "正在验证官方 Web 服务和 Micro 桥接端点。");
        if (!activation.Success ||
            activation.Stage == MicroHarnessDispatchStage.Opening)
        {
            return Failed(
                8,
                activation.Success
                    ? "DeepSeek 网页已启动，但 Micro 桥接没有及时连接。"
                    : activation.Message);
        }

        if (!_registry.MarkSetupCompleted("deepseek-harness"))
        {
            return Failed(8, "DeepSeek 已可用，但首次配置状态无法保存到本机。");
        }

        return new(
            DeepSeekSetupDisposition.ManagedReady,
            $"DeepSeek Harness 已在程序托管环境中就绪，端口为 {port.Value}。",
            8);
    }

    internal static string BuildControlUri(int port) =>
        $"http://127.0.0.1:{port}/__agentcontroller/micro/request";

    internal static string BuildManagedLaunchArguments(int port) =>
        $"--distribution {ManagedDistributionName} " +
        $"--user {ManagedLinuxUser} --exec {ManagedStartPath} --port {port}";

    internal static int? FindAvailablePort(
        int firstPort,
        int lastPort = 3180)
    {
        for (var port = firstPort; port <= lastPort; port++)
        {
            try
            {
                using var listener = new TcpListener(
                    System.Net.IPAddress.Loopback,
                    port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // Try the next loopback-only candidate.
            }
        }
        return null;
    }

    internal static IReadOnlyList<string> ParseDistributionNames(string output) =>
        DeepSeekProcessRunner.NormalizeProcessText(output)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(value => value.Length != 0)
            .ToArray();

    private async Task<bool> ProbeWebAsync(
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1_200));
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUri);
            using var response = await ProbeClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            return true;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                IOException or
                OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private static Uri ToBaseUri(Uri controlUri) => new UriBuilder(controlUri)
    {
        Path = "/",
        Query = string.Empty,
        Fragment = string.Empty,
    }.Uri;

    private static async Task<bool> ProbeBridgeAsync(
        Uri controlUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1_200));
            using var request = new HttpRequestMessage(HttpMethod.Post, controlUri)
            {
                Content = new StringContent(
                    "{\"version\":1,\"source\":\"codex-micro\",\"action\":\"state/read\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
            using var response = await ProbeClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty(
                    "success",
                    out var success) &&
                success.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                IOException or
                JsonException or
                OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }

    private void AdoptProbeEndpoint(DeepSeekEndpointProbe probe)
    {
        var harness = _registry.Resolve("deepseek-harness");
        if (!string.Equals(
                harness.ControlUri,
                probe.ControlUri.AbsoluteUri,
                StringComparison.OrdinalIgnoreCase))
        {
            _registry.UpdateConnectionSettings(
                harness.Id,
                harness.Connection with
                {
                    ControlUri = probe.ControlUri.AbsoluteUri,
                });
        }
    }

    private async Task<(DeepSeekSetupDisposition? Disposition, string Message)>
        EnsureManagedDistributionAsync(
            string wslExecutable,
            string pluginRoot,
            CancellationToken cancellationToken)
    {
        var listed = await _processRunner.RunAsync(
            wslExecutable,
            ["--list", "--quiet"],
            elevated: false,
            cancellationToken);
        if (ParseDistributionNames(listed.StandardOutput).Contains(
                ManagedDistributionName,
                StringComparer.OrdinalIgnoreCase))
        {
            return (null, "Managed WSL distribution is installed.");
        }

        var distributionRoot = Path.Combine(
            _localAppData,
            "CodexMicro",
            "wsl",
            "deepseek");
        var parent = Path.GetDirectoryName(distributionRoot);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return (DeepSeekSetupDisposition.Failed,
                "无法确定程序托管 WSL 的本机目录。");
        }
        Directory.CreateDirectory(parent);
        if (Directory.Exists(distributionRoot) &&
            Directory.EnumerateFileSystemEntries(distributionRoot).Any())
        {
            return (DeepSeekSetupDisposition.Failed,
                $"托管发行版未注册，但目录已包含文件：{distributionRoot}。为避免覆盖数据，程序没有继续。");
        }

        var bundledDistribution = ResolveBundledDistribution(pluginRoot);
        IReadOnlyList<string> installArguments = bundledDistribution is null
            ? [
                "--install",
                ManagedDistributionSource,
                "--name",
                ManagedDistributionName,
                "--location",
                distributionRoot,
                "--version",
                "2",
                "--no-launch",
                "--web-download",
            ]
            : [
                "--install",
                "--from-file",
                bundledDistribution,
                "--name",
                ManagedDistributionName,
                "--location",
                distributionRoot,
                "--no-launch",
            ];
        var installed = await _processRunner.RunAsync(
            wslExecutable,
            installArguments,
            elevated: true,
            cancellationToken);
        if (installed.ExitCode == 1223)
        {
            return (DeepSeekSetupDisposition.Cancelled,
                "已取消 Windows 管理员授权；没有安装托管环境。");
        }

        var after = await _processRunner.RunAsync(
            wslExecutable,
            ["--list", "--quiet"],
            elevated: false,
            cancellationToken);
        if (ParseDistributionNames(after.StandardOutput).Contains(
                ManagedDistributionName,
                StringComparer.OrdinalIgnoreCase))
        {
            return (null, "Managed WSL distribution was installed.");
        }

        if (installed.ExitCode is 0 or 3010)
        {
            return (DeepSeekSetupDisposition.RestartRequired,
                "WSL 系统组件已提交安装。请重启 Windows，之后再次点击 DeepSeek 键；程序会从第 3 步继续。");
        }

        return (DeepSeekSetupDisposition.Failed,
            FirstUsefulMessage(
                installed.StandardError,
                listed.StandardError,
                $"WSL 安装命令退出代码 {installed.ExitCode}。"));
    }

    private async Task<string?> ConvertToWslPathAsync(
        string wslExecutable,
        string windowsPath,
        CancellationToken cancellationToken)
    {
        var converted = await _processRunner.RunAsync(
            wslExecutable,
            [
                "--distribution",
                ManagedDistributionName,
                "--user",
                "root",
                "--exec",
                "wslpath",
                "-a",
                "-u",
                Path.GetFullPath(windowsPath),
            ],
            elevated: false,
            cancellationToken);
        return converted.ExitCode == 0 &&
            !string.IsNullOrWhiteSpace(converted.StandardOutput)
                ? converted.StandardOutput.Trim()
                : null;
    }

    private string? ResolvePluginRoot()
    {
        if (!string.IsNullOrWhiteSpace(_pluginRootOverride))
        {
            var overridden = Path.GetFullPath(_pluginRootOverride);
            return File.Exists(Path.Combine(overridden, "package.json"))
                ? overridden
                : null;
        }

        const string packageRelative = @"plugins\DeepSeekHarness";
        const string repositoryRelative = @"micro-bridge\DeepSeekHarness";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            foreach (var relative in new[] { packageRelative, repositoryRelative })
            {
                var candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(Path.Combine(candidate, "package.json")))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }
        return null;
    }

    private static string? ResolveBundledDistribution(string pluginRoot)
    {
        var packageRoot = Directory.GetParent(pluginRoot)?.Parent?.FullName;
        if (packageRoot is null)
        {
            return null;
        }
        var candidate = Path.Combine(
            packageRoot,
            "payload",
            "deepseek-runtime.wsl");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? ResolveWslExecutable()
    {
        var windowsDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.Windows);
        var candidate = Path.Combine(windowsDirectory, "System32", "wsl.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static DeepSeekSetupResult Failed(int step, string message) =>
        new(DeepSeekSetupDisposition.Failed, message, step);

    private static string FirstUsefulMessage(
        string first,
        string second,
        string fallback) =>
        !string.IsNullOrWhiteSpace(first)
            ? first
            : !string.IsNullOrWhiteSpace(second)
                ? second
                : fallback;
}
