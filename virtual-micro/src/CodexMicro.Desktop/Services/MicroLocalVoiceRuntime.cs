using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Starts and probes the keypad-owned local Qwen service. It never adopts or
/// stops a process that was already running before the keypad connected.
/// </summary>
internal sealed class MicroLocalVoiceRuntime : IAsyncDisposable
{
    private const string StreamingProtocol = "dsh-stream-v1";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex DistributionPattern = new(
        "^[A-Za-z0-9._-]+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _appDirectory;
    private readonly ConcurrentQueue<string> _logTail = new();
    private Process? _ownedProcess;
    private bool _stopOwnedProcess;
    private bool _disposed;
    private int _disposeStarted;

    internal MicroLocalVoiceRuntime(
        HttpClient? httpClient = null,
        string? appDirectory = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = 64 * 1024,
        };
        _appDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(appDirectory)
                ? AppContext.BaseDirectory
                : appDirectory);
    }

    internal async Task WarmUpAsync(
        MicroVoiceProfile settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Provider != MicroVoiceProviders.LocalQwen ||
            !settings.SetupCompleted ||
            settings.LocalStartMode != MicroLocalVoiceStartModes.KeypadStart)
        {
            return;
        }

        await EnsureReadyAsync(settings, cancellationToken);
    }

    internal async Task EnsureReadyAsync(
        MicroVoiceProfile settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        await _gate.WaitAsync(operation.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var healthUri = ValidateHealthUri(settings.LocalHealthUrl);
            if (await IsReadyAsync(healthUri, operation.Token))
            {
                return;
            }

            if (_ownedProcess is { } existing)
            {
                if (!HasExited(existing))
                {
                    await WaitUntilReadyAsync(
                        existing,
                        healthUri,
                        settings.LocalReadyTimeoutSeconds,
                        operation.Token);
                    return;
                }

                existing.Dispose();
                _ownedProcess = null;
            }

            if (settings.LocalStartMode == MicroLocalVoiceStartModes.Manual)
            {
                throw new InvalidOperationException(
                    "Local Qwen ASR is not ready. Start it manually or select a keypad auto-start mode.");
            }

            var startInfo = CreateStartInfo(settings, _appDirectory);
            _logTail.Clear();
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_OutputDataReceived;
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(
                    "The keypad could not start the local Qwen ASR launcher.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ownedProcess = process;
            _stopOwnedProcess = settings.LocalStopWithKeypad;
            try
            {
                await WaitUntilReadyAsync(
                    process,
                    healthUri,
                    settings.LocalReadyTimeoutSeconds,
                    operation.Token);
            }
            catch (OperationCanceledException)
            {
                await StopOwnedProcessAsync();
                throw;
            }
            catch
            {
                await StopOwnedProcessAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<bool> ProbeReadyAsync(
        MicroVoiceProfile settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var healthUri = ValidateHealthUri(settings.LocalHealthUrl);
        return await IsReadyAsync(healthUri, cancellationToken);
    }

    internal static void Validate(MicroVoiceProfile settings)
    {
        MicroVoiceInputService.ValidateStreamingUri(
            settings.LocalStreamUrl,
            requireLoopback: true);
        var healthUri = ValidateHealthUri(settings.LocalHealthUrl);
        var streamUri = new Uri(settings.LocalStreamUrl);
        if (streamUri.Port != healthUri.Port)
        {
            throw new InvalidOperationException(
                "The local Qwen ASR stream and health addresses must use the same port.");
        }
        if (!MicroLocalVoiceStartModes.IsKnown(settings.LocalStartMode))
        {
            throw new InvalidOperationException(
                "The local Qwen ASR start mode is unsupported.");
        }
        if (settings.LocalStartMode != MicroLocalVoiceStartModes.Manual &&
            (streamUri.Scheme != "ws" || healthUri.Scheme != "http"))
        {
            throw new InvalidOperationException(
                "The bundled Qwen launcher uses loopback ws:// and http://. Use manual mode for a custom TLS terminator.");
        }
        if (settings.LocalReadyTimeoutSeconds is < 10 or > 3600)
        {
            throw new InvalidOperationException(
                "The local Qwen ASR ready timeout must be between 10 and 3600 seconds.");
        }
        if (string.IsNullOrWhiteSpace(settings.LocalLauncherPath))
        {
            throw new InvalidOperationException(
                "The local Qwen ASR launcher path is required.");
        }
        if (string.IsNullOrWhiteSpace(settings.LocalWorkingDirectory))
        {
            throw new InvalidOperationException(
                "The local Qwen ASR working directory is required.");
        }
        if (!DistributionPattern.IsMatch(settings.LocalDistribution ?? string.Empty))
        {
            throw new InvalidOperationException(
                "The WSL distribution name may contain only letters, numbers, dot, underscore, and hyphen.");
        }
        if (ContainsLineBreak(settings.LocalPythonPath) ||
            ContainsLineBreak(settings.LocalModel))
        {
            throw new InvalidOperationException(
                "Local Qwen ASR process values cannot contain line breaks.");
        }
    }

    internal static Uri ValidateHealthUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Local Qwen ASR health checks must use an unauthenticated loopback http:// or https:// address.");
        }

        return uri;
    }

    internal static string ResolvePortablePath(
        string value,
        string appDirectory,
        string? localAppData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        var localData = string.IsNullOrWhiteSpace(localAppData)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppData;
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim())
            .Replace("{AppDir}", appDirectory, StringComparison.OrdinalIgnoreCase)
            .Replace("{LocalAppData}", localData, StringComparison.OrdinalIgnoreCase);
        var combined = Path.IsPathFullyQualified(expanded)
            ? expanded
            : Path.Combine(appDirectory, expanded);
        return Path.GetFullPath(combined);
    }

    internal static ProcessStartInfo CreateStartInfo(
        MicroVoiceProfile settings,
        string appDirectory)
    {
        Validate(settings);
        var streamUri = new Uri(settings.LocalStreamUrl);
        var launcher = ResolvePortablePath(
            settings.LocalLauncherPath,
            appDirectory);
        var workingDirectory = ResolvePortablePath(
            settings.LocalWorkingDirectory,
            appDirectory);
        if (!File.Exists(launcher))
        {
            throw new FileNotFoundException(
                "The keypad-owned Qwen ASR launcher was not found.",
                launcher);
        }
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"The keypad-owned Qwen ASR working directory was not found: {workingDirectory}");
        }
        if (!string.Equals(
                Path.GetExtension(launcher),
                ".ps1",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The local Qwen ASR launcher must be a PowerShell .ps1 file.");
        }

        var result = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            launcher,
            "-Distribution",
            settings.LocalDistribution,
            "-Model",
            settings.LocalModel,
            "-Port",
            streamUri.Port.ToString(CultureInfo.InvariantCulture),
        })
        {
            result.ArgumentList.Add(argument);
        }
        if (!string.IsNullOrWhiteSpace(settings.LocalPythonPath))
        {
            result.ArgumentList.Add("-PythonPath");
            result.ArgumentList.Add(settings.LocalPythonPath.Trim());
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        await _gate.WaitAsync();
        try
        {
            _disposed = true;
            try
            {
                await StopOwnedProcessAsync(force: _stopOwnedProcess);
            }
            finally
            {
                if (_ownsHttpClient)
                {
                    _httpClient.Dispose();
                }
                _lifetime.Dispose();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task WaitUntilReadyAsync(
        Process process,
        Uri healthUri,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExited(process))
            {
                throw new InvalidOperationException(
                    BuildFailureMessage(
                        $"Local Qwen ASR exited before becoming ready (exit code {SafeExitCode(process)})."));
            }
            if (await IsReadyAsync(healthUri, cancellationToken))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        throw new TimeoutException(BuildFailureMessage(
            $"Local Qwen ASR did not become ready within {timeoutSeconds} seconds."));
    }

    private async Task<bool> IsReadyAsync(
        Uri healthUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            probe.CancelAfter(ProbeTimeout);
            using var response = await _httpClient.GetAsync(
                healthUri,
                HttpCompletionOption.ResponseHeadersRead,
                probe.Token);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                probe.Token);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: probe.Token);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("ready", out var ready) &&
                ready.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("protocol", out var protocol) &&
                protocol.ValueKind == JsonValueKind.String &&
                string.Equals(
                    protocol.GetString(),
                    StreamingProtocol,
                    StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private async Task StopOwnedProcessAsync(bool force = true)
    {
        var process = _ownedProcess;
        _ownedProcess = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (force && !HasExited(process))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(
                            TimeSpan.FromSeconds(8));
                    }
                    catch (TimeoutException)
                    {
                        // Process disposal below releases our local handle.
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the readiness check and kill.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Windows may already have torn down the process tree.
                }
            }
        }
        finally
        {
            process.OutputDataReceived -= Process_OutputDataReceived;
            process.ErrorDataReceived -= Process_OutputDataReceived;
            process.Dispose();
        }
    }

    private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Data))
        {
            return;
        }

        _logTail.Enqueue(e.Data.Trim());
        while (_logTail.Count > 16)
        {
            _logTail.TryDequeue(out _);
        }
    }

    private string BuildFailureMessage(string headline)
    {
        var log = string.Join(Environment.NewLine, _logTail.ToArray());
        return log.Length == 0
            ? headline
            : $"{headline}{Environment.NewLine}{log}";
    }

    private static bool ContainsLineBreak(string? value) =>
        value?.IndexOfAny(['\r', '\n']) >= 0;

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
