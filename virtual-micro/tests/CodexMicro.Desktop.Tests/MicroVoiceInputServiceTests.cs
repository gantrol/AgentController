using CodexMicro.Desktop.Services;
using System.Diagnostics;
using System.Net;
using System.Text;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class MicroVoiceInputServiceTests
{
    private sealed class MemoryCredentialStore : IMicroVoiceCredentialStore
    {
        private readonly Dictionary<(string Scope, string Provider), string> _values = [];

        internal int WriteCount { get; private set; }

        public string? Read(string scope, string provider) =>
            _values.GetValueOrDefault((scope, provider));

        public void Write(string scope, string provider, string value)
        {
            WriteCount++;
            _values[(scope, provider)] = value;
        }

        public void Delete(string scope, string provider) =>
            _values.Remove((scope, provider));
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    [Fact]
    public async Task RequiresKeypadSetupBeforeStartingCapture()
    {
        var profile = MicroProfileSettings.CreateTransient();
        await using var voice = new MicroVoiceInputService(profile);

        var result = await voice.StartAsync("deepseek-session");

        Assert.False(result.Success);
        Assert.True(result.SetupRequired);
        Assert.Equal(
            MicroVoiceRecoveryAction.OpenVoiceSettings,
            result.RecoveryAction);
        Assert.False(voice.Current.Active);
        Assert.Equal("error", voice.Current.Phase);
        Assert.Equal("deepseek-session", voice.Current.SessionId);
    }

    [Fact]
    public async Task FailedProviderProbeKeepsLastVerifiedProfileAndCredential()
    {
        var verified = MicroVoiceProfile.Default with
        {
            SetupCompleted = true,
        };
        var profile = MicroProfileSettings.CreateTransient();
        profile.SetVoiceSettings(verified);
        var credentials = new MemoryCredentialStore();
        await using var voice = new MicroVoiceInputService(
            profile,
            credentials);
        var candidate = verified with
        {
            Provider = MicroVoiceProviders.RemoteWebSocket,
            SetupCompleted = false,
            RemoteUrl = "ws://127.0.0.1:1/v1/stream",
            RemoteModel = "candidate-model",
        };

        await Assert.ThrowsAnyAsync<Exception>(() =>
            voice.TestAndSaveAsync(candidate, "candidate-secret"));

        Assert.Equal(verified, profile.Current.VoiceSettings);
        Assert.Equal(0, credentials.WriteCount);
    }

    [Fact]
    public void LocalQwenDefaultsDoNotStoreMachineSpecificAbsolutePaths()
    {
        var settings = MicroVoiceProfile.Default;

        Assert.StartsWith("{AppDir}", settings.LocalLauncherPath);
        Assert.StartsWith("{AppDir}", settings.LocalWorkingDirectory);
        Assert.False(Path.IsPathFullyQualified(settings.LocalLauncherPath));
        Assert.False(Path.IsPathFullyQualified(settings.LocalWorkingDirectory));
        Assert.Equal(string.Empty, settings.LocalPythonPath);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void StreamingVoiceReadinessRequiresAnActiveWindowsMicrophone(
        int deviceCount,
        bool expectedAvailable)
    {
        var availability = MicroVoiceCaptureDevices.FromDeviceCount(deviceCount);

        Assert.Equal(expectedAvailable, availability.Available);
        Assert.Equal(deviceCount, availability.DeviceCount);
        if (!expectedAvailable)
        {
            Assert.Contains("microphone", availability.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Connect or enable", availability.Message);
        }
    }

    [Fact]
    public void MissingCaptureDeviceProducesAnActionableRecovery()
    {
        var error = new InvalidOperationException(
            MicroVoiceCaptureDevices.MissingDeviceMessage);

        Assert.True(MicroVoiceInputService.IsCaptureDeviceFailure(error));
        Assert.Equal(
            MicroVoiceRecoveryAction.ConnectOrEnableMicrophone,
            MicroVoiceInputService.ResolveRecoveryAction(
                MicroVoiceProfile.Default with
                {
                    Provider = MicroVoiceProviders.LocalQwen,
                },
                error));
    }

    [Fact]
    public void LocalLinkFailureRequestsAServiceRestart()
    {
        Assert.Equal(
            MicroVoiceRecoveryAction.RestartVoiceService,
            MicroVoiceInputService.ResolveRecoveryAction(
                MicroVoiceProfile.Default with
                {
                    Provider = MicroVoiceProviders.LocalQwen,
                },
                new InvalidOperationException("stream handshake failed")));
    }

    [Fact]
    public void WarmUpTrackerIgnoresEquivalentVoiceProfiles()
    {
        var tracker = new MicroVoiceWarmUpSettingsTracker();
        var settings = MicroVoiceProfile.Default with
        {
            Provider = MicroVoiceProviders.LocalQwen,
            SetupCompleted = true,
        };

        Assert.True(tracker.Update(settings));
        Assert.False(tracker.Update(settings with { }));
        Assert.True(tracker.Update(settings with
        {
            LocalModel = "Qwen/Qwen3-ASR-1.7B",
        }));
        Assert.False(tracker.Update(settings with
        {
            LocalModel = "Qwen/Qwen3-ASR-1.7B",
        }));
    }

    [Theory]
    [InlineData("ws://speech.example.test/v1/stream")]
    [InlineData("http://127.0.0.1:8765/v1/stream")]
    [InlineData("wss://user:secret@speech.example.test/v1/stream")]
    public void RejectsUnsafeRemoteStreamingAddresses(string value)
    {
        var settings = new MicroVoiceProfile(
            Provider: MicroVoiceProviders.RemoteWebSocket,
            RemoteUrl: value);

        Assert.Throws<InvalidOperationException>(() =>
            MicroVoiceInputService.Validate(settings));
    }

    [Fact]
    public void LocalStreamingAddressMustRemainOnLoopback()
    {
        var settings = new MicroVoiceProfile(
            Provider: MicroVoiceProviders.LocalQwen,
            LocalStreamUrl: "wss://speech.example.test/v1/stream");

        Assert.Throws<InvalidOperationException>(() =>
            MicroVoiceInputService.Validate(settings));
    }

    [Fact]
    public void LocalStreamAndHealthMustUseTheSamePort()
    {
        var settings = new MicroVoiceProfile(
            Provider: MicroVoiceProviders.LocalQwen,
            LocalStreamUrl: "ws://127.0.0.1:8765/v1/stream",
            LocalHealthUrl: "http://127.0.0.1:9876/health");

        Assert.Throws<InvalidOperationException>(() =>
            MicroVoiceInputService.Validate(settings));
    }

    [Theory]
    [InlineData("http://speech.example.test/health")]
    [InlineData("ftp://127.0.0.1/health")]
    [InlineData("http://user:secret@127.0.0.1/health")]
    public void LocalHealthAddressMustBeUnauthenticatedLoopback(string value)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MicroLocalVoiceRuntime.ValidateHealthUri(value));
    }

    [Fact]
    public void PortablePathsResolveAgainstRuntimeDirectories()
    {
        var appDirectory = Path.GetFullPath(Path.Combine("runtime", "keypad"));
        var localData = Path.GetFullPath(Path.Combine("runtime", "profile"));

        Assert.Equal(
            Path.Combine(appDirectory, "voice", "start.ps1"),
            MicroLocalVoiceRuntime.ResolvePortablePath(
                "{AppDir}\\voice\\start.ps1",
                appDirectory,
                localData));
        Assert.Equal(
            Path.Combine(localData, "CodexMicro", "voice"),
            MicroLocalVoiceRuntime.ResolvePortablePath(
                "{LocalAppData}\\CodexMicro\\voice",
                appDirectory,
                localData));
        Assert.Equal(
            Path.Combine(appDirectory, "voice", "start.ps1"),
            MicroLocalVoiceRuntime.ResolvePortablePath(
                "voice\\start.ps1",
                appDirectory,
                localData));
    }

    [Fact]
    public void QwenLauncherUsesArgumentListAndConfiguredPort()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-voice-runtime-tests",
            Guid.NewGuid().ToString("N"));
        var voiceDirectory = Path.Combine(directory, "voice");
        var launcher = Path.Combine(
            voiceDirectory,
            "start-qwen3-asr-stream.ps1");
        try
        {
            Directory.CreateDirectory(voiceDirectory);
            File.WriteAllText(launcher, "# test launcher");
            var settings = MicroVoiceProfile.Default with
            {
                Provider = MicroVoiceProviders.LocalQwen,
                LocalStreamUrl = "ws://127.0.0.1:9876/v1/stream",
                LocalHealthUrl = "http://127.0.0.1:9876/health",
                LocalLauncherPath =
                    "{AppDir}\\voice\\start-qwen3-asr-stream.ps1",
                LocalWorkingDirectory = "{AppDir}\\voice",
                LocalDistribution = "Ubuntu-24.04",
                LocalPythonPath = "/home/user/.venvs/qwen/bin/python",
            };

            ProcessStartInfo startInfo =
                MicroLocalVoiceRuntime.CreateStartInfo(settings, directory);
            var arguments = startInfo.ArgumentList.ToArray();

            Assert.Equal("powershell.exe", startInfo.FileName);
            Assert.False(startInfo.UseShellExecute);
            Assert.True(startInfo.CreateNoWindow);
            Assert.Equal(voiceDirectory, startInfo.WorkingDirectory);
            Assert.Contains(launcher, arguments);
            Assert.Contains("Ubuntu-24.04", arguments);
            Assert.Contains("9876", arguments);
            Assert.Contains("/home/user/.venvs/qwen/bin/python", arguments);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReadyHealthEndpointDoesNotLaunchAProcess()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => new(
            HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"ready\":true,\"protocol\":\"dsh-stream-v1\"}",
                Encoding.UTF8,
                "application/json"),
        }));
        await using var runtime = new MicroLocalVoiceRuntime(client);
        var settings = MicroVoiceProfile.Default with
        {
            Provider = MicroVoiceProviders.LocalQwen,
            LocalStartMode = MicroLocalVoiceStartModes.Manual,
        };

        await runtime.EnsureReadyAsync(settings);
    }

    [Fact]
    public async Task ExistingExternalQwenServiceIsNeverRestartedByTheKeypad()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => new(
            HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"ready\":true,\"protocol\":\"dsh-stream-v1\"}",
                Encoding.UTF8,
                "application/json"),
        }));
        await using var runtime = new MicroLocalVoiceRuntime(client);
        var settings = MicroVoiceProfile.Default with
        {
            Provider = MicroVoiceProviders.LocalQwen,
            LocalStartMode = MicroLocalVoiceStartModes.OnDemand,
        };

        await runtime.EnsureReadyAsync(settings);

        Assert.False(await runtime.TryRestartOwnedAsync(settings));
    }

    [Fact]
    public async Task KeypadOwnedQwenLauncherCanBeRestartedAndReprobed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-owned-voice-runtime-tests",
            Guid.NewGuid().ToString("N"));
        var voiceDirectory = Path.Combine(directory, "voice");
        var launcher = Path.Combine(voiceDirectory, "test-qwen-launcher.ps1");
        try
        {
            Directory.CreateDirectory(voiceDirectory);
            File.WriteAllText(
                launcher,
                "param([string]$Distribution,[string]$Model,[int]$Port)\r\nStart-Sleep -Seconds 60\r\n");
            var probeCount = 0;
            using var client = new HttpClient(new StubHttpHandler(_ =>
            {
                var probe = Interlocked.Increment(ref probeCount);
                var ready = probe is 2 or >= 4;
                return new HttpResponseMessage(
                    ready ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable)
                {
                    Content = ready
                        ? new StringContent(
                            "{\"ready\":true,\"protocol\":\"dsh-stream-v1\"}",
                            Encoding.UTF8,
                            "application/json")
                        : new StringContent(string.Empty),
                };
            }));
            await using (var runtime = new MicroLocalVoiceRuntime(
                client,
                directory))
            {
                var settings = MicroVoiceProfile.Default with
                {
                    Provider = MicroVoiceProviders.LocalQwen,
                    LocalStartMode = MicroLocalVoiceStartModes.OnDemand,
                    LocalLauncherPath = "{AppDir}\\voice\\test-qwen-launcher.ps1",
                    LocalWorkingDirectory = "{AppDir}\\voice",
                    LocalReadyTimeoutSeconds = 10,
                };

                await runtime.EnsureReadyAsync(settings);

                Assert.True(await runtime.TryRestartOwnedAsync(settings));
                Assert.True(probeCount >= 4);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task VoiceReadinessProbeAcceptsTheStreamingProtocol()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => new(
            HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"ready\":true,\"protocol\":\"dsh-stream-v1\"}",
                Encoding.UTF8,
                "application/json"),
        }));
        await using var runtime = new MicroLocalVoiceRuntime(client);

        var ready = await runtime.ProbeReadyAsync(
            MicroVoiceProfile.Default with
            {
                Provider = MicroVoiceProviders.LocalQwen,
            });

        Assert.True(ready);
    }

    [Theory]
    [InlineData("{\"ready\":false,\"protocol\":\"dsh-stream-v1\"}")]
    [InlineData("{\"ready\":true,\"protocol\":\"legacy\"}")]
    [InlineData("not-json")]
    public async Task VoiceReadinessProbeRejectsUnreadyOrUnexpectedServices(
        string payload)
    {
        using var client = new HttpClient(new StubHttpHandler(_ => new(
            HttpStatusCode.OK)
        {
            Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json"),
        }));
        await using var runtime = new MicroLocalVoiceRuntime(client);

        var ready = await runtime.ProbeReadyAsync(
            MicroVoiceProfile.Default with
            {
                Provider = MicroVoiceProviders.LocalQwen,
            });

        Assert.False(ready);
    }

    [Fact]
    public async Task ManualModeReportsWhenHealthEndpointIsNotReady()
    {
        using var client = new HttpClient(new StubHttpHandler(_ => new(
            HttpStatusCode.ServiceUnavailable)));
        await using var runtime = new MicroLocalVoiceRuntime(client);
        var settings = MicroVoiceProfile.Default with
        {
            Provider = MicroVoiceProviders.LocalQwen,
            LocalStartMode = MicroLocalVoiceStartModes.Manual,
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.EnsureReadyAsync(settings));

        Assert.Contains("Start it manually", error.Message);
    }
}
