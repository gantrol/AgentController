using System.ComponentModel;
using System.Diagnostics;
using AgentController.Application.Diagnostics;

namespace AgentController.Adapters.DeveloperTools;

public sealed class DotNetBuildDiagnosticProvider :
    IDeveloperDiagnosticProvider
{
    private static readonly HashSet<string> SupportedTargetExtensions = new(
        [".sln", ".slnx", ".csproj", ".fsproj", ".vbproj"],
        StringComparer.OrdinalIgnoreCase);

    private readonly DotNetBuildDiagnosticOptions _options;

    public DotNetBuildDiagnosticProvider(
        DotNetBuildDiagnosticOptions? options = null)
    {
        _options = options ?? new();
        _options.Validate();
    }

    public string Id => "dotnet-build";

    public async ValueTask<DeveloperDiagnosticProviderCapability> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Path.IsPathFullyQualified(_options.DotNetExecutable) &&
            !File.Exists(_options.DotNetExecutable))
        {
            return new(
                Id,
                DeveloperDiagnosticProviderStatus.Misconfigured,
                ReasonCode: "developer-diagnostics.dotnet-not-found");
        }

        var startInfo = CreateBaseStartInfo();
        startInfo.ArgumentList.Add("--version");
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return UnavailableCapability();
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            return UnavailableCapability();
        }

        var standardOutput = BoundedTextCapture.ReadAsync(
            process.StandardOutput,
            4 * 1024);
        var standardError = BoundedTextCapture.ReadAsync(
            process.StandardError,
            4 * 1024);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return UnavailableCapability(
                "developer-diagnostics.dotnet-probe-timeout");
        }

        var output = await standardOutput.ConfigureAwait(false);
        _ = await standardError.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            return UnavailableCapability();
        }

        var version = output.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
        return new(
            Id,
            DeveloperDiagnosticProviderStatus.Available,
            version);
    }

    public async Task<DeveloperDiagnosticRun> AnalyzeAsync(
        DeveloperDiagnosticRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var buildId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        if (cancellationToken.IsCancellationRequested)
        {
            return EmptyRun(
                request,
                buildId,
                startedAt,
                DeveloperDiagnosticRunStatus.Cancelled,
                "developer-diagnostics.cancelled");
        }

        if (!TryValidate(
                request,
                buildId,
                out var validated,
                out var validationFailure))
        {
            return EmptyRun(
                request,
                buildId,
                startedAt,
                DeveloperDiagnosticRunStatus.Misconfigured,
                validationFailure);
        }

        try
        {
            Directory.CreateDirectory(validated.ArtifactDirectory);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            return EmptyRun(
                request,
                buildId,
                startedAt,
                DeveloperDiagnosticRunStatus.Misconfigured,
                "developer-diagnostics.artifact-directory-unavailable");
        }
        var sarifPath = Path.Combine(
            validated.ArtifactDirectory,
            "build.sarif");
        var binaryLogPath = request.CaptureBuildTrace
            ? Path.Combine(validated.ArtifactDirectory, "build.binlog")
            : null;
        var standardOutputPath = Path.Combine(
            validated.ArtifactDirectory,
            "build.stdout.log");
        var standardErrorPath = Path.Combine(
            validated.ArtifactDirectory,
            "build.stderr.log");

        var startInfo = CreateBuildStartInfo(
            validated,
            request,
            sarifPath,
            binaryLogPath);
        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                stopwatch.Stop();
                return EmptyRun(
                    request,
                    buildId,
                    startedAt,
                    DeveloperDiagnosticRunStatus.ToolUnavailable,
                    "developer-diagnostics.dotnet-start-failed",
                    stopwatch.Elapsed);
            }
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            stopwatch.Stop();
            return EmptyRun(
                request,
                buildId,
                startedAt,
                DeveloperDiagnosticRunStatus.ToolUnavailable,
                "developer-diagnostics.dotnet-not-found",
                stopwatch.Elapsed);
        }

        var standardOutputTask = BoundedTextCapture.ReadAsync(
            process.StandardOutput,
            _options.MaxCapturedCharacters);
        var standardErrorTask = BoundedTextCapture.ReadAsync(
            process.StandardError,
            _options.MaxCapturedCharacters);
        var timeoutValue = request.Timeout ?? _options.DefaultTimeout;
        using var timeout = new CancellationTokenSource(timeoutValue);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        DeveloperDiagnosticRunStatus? interruptedStatus = null;
        string? interruptionCode = null;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            interruptedStatus = cancellationToken.IsCancellationRequested
                ? DeveloperDiagnosticRunStatus.Cancelled
                : DeveloperDiagnosticRunStatus.TimedOut;
            interruptionCode = cancellationToken.IsCancellationRequested
                ? "developer-diagnostics.cancelled"
                : "developer-diagnostics.timeout";
            TryKill(process);
            await WaitAfterKillAsync(process).ConfigureAwait(false);
        }

        stopwatch.Stop();
        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);
        string? artifactFailureCode = null;
        try
        {
            await PersistOutputAsync(
                    standardOutputPath,
                    standardOutput.Text,
                    standardErrorPath,
                    standardError.Text)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            artifactFailureCode =
                "developer-diagnostics.artifact-write-failed";
        }

        var observations = new List<DeveloperDiagnosticObservation>();
        var sarif = await SarifDiagnosticParser.ParseAsync(
                sarifPath,
                validated.WorkspaceRoot,
                buildId,
                _options.MaxSarifBytes,
                CancellationToken.None)
            .ConfigureAwait(false);
        observations.AddRange(sarif.Diagnostics);
        observations.AddRange(MsBuildTextDiagnosticParser.Parse(
            standardOutput.Text,
            validated.WorkspaceRoot,
            buildId,
            standardOutputPath));
        observations.AddRange(MsBuildTextDiagnosticParser.Parse(
            standardError.Text,
            validated.WorkspaceRoot,
            buildId,
            standardErrorPath));

        int? exitCode = process.HasExited ? process.ExitCode : null;
        var status = interruptedStatus ??
            (exitCode == 0 &&
                sarif.FailureCode is null &&
                artifactFailureCode is null
                ? DeveloperDiagnosticRunStatus.Succeeded
                : DeveloperDiagnosticRunStatus.Failed);
        var failureCode = interruptionCode ??
            sarif.FailureCode ??
            artifactFailureCode ??
            (exitCode == 0
                ? null
                : "developer-diagnostics.build-failed");
        var performance = new[]
        {
            new DeveloperPerformanceMeasurement(
                "build.total",
                stopwatch.Elapsed,
                Id,
                buildId,
                binaryLogPath is not null && File.Exists(binaryLogPath)
                    ? binaryLogPath
                    : null),
        };

        return new(
            Id,
            buildId,
            status,
            new(
                validated.WorkspaceRoot,
                validated.TargetPath,
                validated.Configuration,
                request.DocumentVersion),
            startedAt,
            stopwatch.Elapsed,
            DeveloperDiagnosticAggregator.Aggregate(observations),
            performance,
            exitCode,
            failureCode,
            standardOutput.Truncated || standardError.Truncated,
            standardOutput.Text,
            standardError.Text);
    }

    private ProcessStartInfo CreateBaseStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.DotNetExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        startInfo.Environment["VSLANG"] = "1033";
        return startInfo;
    }

    private ProcessStartInfo CreateBuildStartInfo(
        ValidatedRequest validated,
        DeveloperDiagnosticRequest request,
        string sarifPath,
        string? binaryLogPath)
    {
        var startInfo = CreateBaseStartInfo();
        startInfo.WorkingDirectory = validated.WorkspaceRoot;
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(validated.TargetPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(validated.Configuration);
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity:minimal");
        startInfo.ArgumentList.Add("--tl:off");
        startInfo.ArgumentList.Add($"-p:ErrorLog={sarifPath}");
        if (request.NoRestore)
        {
            startInfo.ArgumentList.Add("--no-restore");
        }

        if (binaryLogPath is not null)
        {
            startInfo.ArgumentList.Add(
                $"-bl:{binaryLogPath};ProjectImports=None");
        }

        return startInfo;
    }

    private bool TryValidate(
        DeveloperDiagnosticRequest request,
        string buildId,
        out ValidatedRequest validated,
        out string failureCode)
    {
        validated = null!;
        failureCode = string.Empty;
        if (!request.AllowProjectExecution)
        {
            failureCode =
                "developer-diagnostics.execution-consent-required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceRoot) ||
            string.IsNullOrWhiteSpace(request.TargetPath))
        {
            failureCode = "developer-diagnostics.path-required";
            return false;
        }

        string workspaceRoot;
        string targetPath;
        string artifactDirectory;
        try
        {
            workspaceRoot = Path.GetFullPath(request.WorkspaceRoot);
            targetPath = Path.IsPathFullyQualified(request.TargetPath)
                ? Path.GetFullPath(request.TargetPath)
                : Path.GetFullPath(request.TargetPath, workspaceRoot);
            artifactDirectory = string.IsNullOrWhiteSpace(
                request.ArtifactDirectory)
                ? Path.Combine(
                    workspaceRoot,
                    ".artifacts",
                    "developer-diagnostics",
                    buildId)
                : Path.IsPathFullyQualified(request.ArtifactDirectory)
                    ? Path.GetFullPath(request.ArtifactDirectory)
                    : Path.GetFullPath(
                        request.ArtifactDirectory,
                        workspaceRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            failureCode = "developer-diagnostics.path-invalid";
            return false;
        }

        if (!Directory.Exists(workspaceRoot))
        {
            failureCode = "developer-diagnostics.workspace-not-found";
            return false;
        }

        if (!File.Exists(targetPath) ||
            !DiagnosticPath.IsWithin(workspaceRoot, targetPath) ||
            !SupportedTargetExtensions.Contains(
                Path.GetExtension(targetPath)))
        {
            failureCode = "developer-diagnostics.target-not-allowed";
            return false;
        }

        if (!DiagnosticPath.IsWithin(workspaceRoot, artifactDirectory) ||
            artifactDirectory.Contains(';', StringComparison.Ordinal) ||
            artifactDirectory.Contains('\r', StringComparison.Ordinal) ||
            artifactDirectory.Contains('\n', StringComparison.Ordinal))
        {
            failureCode = "developer-diagnostics.artifact-path-not-allowed";
            return false;
        }

        var configuration = request.Configuration.Trim();
        if (configuration.Length is 0 or > 64 ||
            configuration.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '_' and not '-' and not '.'))
        {
            failureCode = "developer-diagnostics.configuration-invalid";
            return false;
        }

        var timeout = request.Timeout ?? _options.DefaultTimeout;
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(30))
        {
            failureCode = "developer-diagnostics.timeout-invalid";
            return false;
        }

        validated = new(
            workspaceRoot,
            targetPath,
            artifactDirectory,
            configuration);
        return true;
    }

    private DeveloperDiagnosticProviderCapability UnavailableCapability(
        string reasonCode = "developer-diagnostics.dotnet-unavailable") =>
        new(
            Id,
            DeveloperDiagnosticProviderStatus.Unsupported,
            ReasonCode: reasonCode);

    private DeveloperDiagnosticRun EmptyRun(
        DeveloperDiagnosticRequest request,
        string buildId,
        DateTimeOffset startedAt,
        DeveloperDiagnosticRunStatus status,
        string? failureCode,
        TimeSpan? elapsed = null) =>
        new(
            Id,
            buildId,
            status,
            new(
                request.WorkspaceRoot,
                request.TargetPath,
                request.Configuration,
                request.DocumentVersion),
            startedAt,
            elapsed ?? TimeSpan.Zero,
            [],
            [],
            FailureCode: failureCode);

    private static async Task PersistOutputAsync(
        string standardOutputPath,
        string standardOutput,
        string standardErrorPath,
        string standardError)
    {
        await File.WriteAllTextAsync(
                standardOutputPath,
                standardOutput)
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                standardErrorPath,
                standardError)
            .ConfigureAwait(false);
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
        }
    }

    private sealed record ValidatedRequest(
        string WorkspaceRoot,
        string TargetPath,
        string ArtifactDirectory,
        string Configuration);
}
