using CodexMicro.Desktop.Services;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class MicroHarnessRegistryTests
{
    private sealed class InlineProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    [Fact]
    public void UsesFrequencyBasedFourKeyDefaultsAndMigratesTheLegacyMap()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-default-migration-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "KeyMaps": {
                    "deepseek-harness": {
                      "ACT06": "session/new",
                      "ACT07": "none",
                      "ACT08": "none",
                      "ACT09": "session/fork",
                      "ACT10_ACT11": "none",
                      "ACT10": "none",
                      "ACT11": "none",
                      "JOY_UP": "session/new",
                      "JOY_DOWN": "turn/cancel",
                      "JOY_LEFT": "session/previous",
                      "JOY_RIGHT": "session/next"
                    }
                  }
                }
                """);

            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            var map = registry.ResolveKeyMap("deepseek-harness");

            Assert.Equal(
                MicroHarnessActionIds.NewSession,
                map.Resolve(MicroHarnessControlIds.Action06));
            Assert.Equal(
                MicroHarnessActionIds.ToggleConversationView,
                map.Resolve(MicroHarnessControlIds.Action07));
            Assert.Equal(
                MicroHarnessActionIds.CancelTurn,
                map.Resolve(MicroHarnessControlIds.Action08));
            Assert.Equal(
                MicroHarnessActionIds.ForkSession,
                map.Resolve(MicroHarnessControlIds.Action09));
            Assert.Equal(
                MicroHarnessActionIds.VoiceDictation,
                map.Resolve(MicroHarnessControlIds.VoiceWide));
            Assert.Equal(
                MicroHarnessActionIds.PreviousSession,
                map.Resolve(MicroHarnessControlIds.JoystickUp));
            Assert.Equal(
                MicroHarnessActionIds.NextSession,
                map.Resolve(MicroHarnessControlIds.JoystickDown));
            Assert.Equal(
                MicroHarnessActionIds.ToggleSidebar,
                map.Resolve(MicroHarnessControlIds.JoystickLeft));
            Assert.Equal(
                MicroHarnessActionIds.OpenDetails,
                map.Resolve(MicroHarnessControlIds.JoystickRight));
            Assert.False(registry.IsSetupCompleted("deepseek-harness"));
            Assert.Equal(
                MicroHarnessKnobModes.ComposerNavigation,
                registry.ResolveKnobMode("deepseek-harness"));
            Assert.True(registry.UpdateKnobMode(
                "deepseek-harness",
                MicroHarnessKnobModes.RecentSessions));
            var migrated = File.ReadAllText(settingsPath);
            Assert.Contains("view/toggle-chat-trajectory", migrated);
            Assert.Contains("turn/cancel", migrated);
            Assert.Contains("session/previous", migrated);
            Assert.Contains("session/next", migrated);
            Assert.Contains("layout/toggle-sidebar", migrated);
            Assert.Contains("layout/open-details", migrated);
            Assert.Contains("recent-sessions", migrated);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MigratesOnlyTheFormerJoystickDefaultsBesideCustomKeys()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-joystick-migration-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "KeyMaps": {
                    "deepseek-harness": {
                      "ACT06": "session/archive",
                      "ACT07": "view/toggle-chat-trajectory",
                      "ACT08": "interaction/reject",
                      "ACT09": "session/fork",
                      "ACT10_ACT11": "voice/dictation",
                      "ACT10": "none",
                      "ACT11": "none",
                      "JOY_UP": "session/new",
                      "JOY_DOWN": "turn/cancel",
                      "JOY_LEFT": "session/previous",
                      "JOY_RIGHT": "session/next"
                    }
                  }
                }
                """);

            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            var map = registry.ResolveKeyMap("deepseek-harness");

            Assert.Equal(
                MicroHarnessActionIds.ArchiveSession,
                map.Resolve(MicroHarnessControlIds.Action06));
            Assert.Equal(
                MicroHarnessActionIds.RejectInteraction,
                map.Resolve(MicroHarnessControlIds.Action08));
            Assert.Equal(
                MicroHarnessActionIds.None,
                map.Resolve(MicroHarnessControlIds.VoiceLeft));
            Assert.Equal(
                MicroHarnessActionIds.PreviousSession,
                map.Resolve(MicroHarnessControlIds.JoystickUp));
            Assert.Equal(
                MicroHarnessActionIds.NextSession,
                map.Resolve(MicroHarnessControlIds.JoystickDown));
            Assert.Equal(
                MicroHarnessActionIds.ToggleSidebar,
                map.Resolve(MicroHarnessControlIds.JoystickLeft));
            Assert.Equal(
                MicroHarnessActionIds.OpenDetails,
                map.Resolve(MicroHarnessControlIds.JoystickRight));

            var persisted = File.ReadAllText(settingsPath);
            Assert.Contains("session/archive", persisted);
            Assert.Contains("interaction/reject", persisted);
            Assert.Contains("layout/toggle-sidebar", persisted);
            Assert.Contains("layout/open-details", persisted);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PersistsFirstUseSetupCompletionPerHarness()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-onboarding-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            Assert.False(registry.IsSetupCompleted("deepseek-harness"));
            Assert.True(registry.MarkSetupCompleted("deepseek-harness"));

            var restored = new MicroHarnessRegistry(settingsPath: settingsPath);
            Assert.True(restored.IsSetupCompleted("deepseek-harness"));
            Assert.False(restored.IsSetupCompleted("codex"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DiscoversFutureHarnessManifestBesideBuiltInTargets()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "future.json"),
                """
                {
                  "id": "future-harness",
                  "displayName": "Future Harness",
                  "description": "Direct test adapter",
                  "pipeName": "future-harness-micro-v1"
                }
                """);

            var registry = new MicroHarnessRegistry(
                manifestDirectory: directory);

            Assert.Equal("Codex", registry.Definitions[0].DisplayName);
            Assert.Contains(registry.Definitions, item =>
                item.Id == "deepseek-harness");
            var deepSeek = registry.Resolve("deepseek-harness");
            Assert.Equal("wsl.exe", Path.GetFileName(deepSeek.Connection.Executable));
            Assert.Contains("start-dsh-wsl.sh", deepSeek.Connection.Arguments);
            Assert.Equal(
                "http://127.0.0.1:3080/__agentcontroller/micro/request",
                deepSeek.ControlUri);
            var future = Assert.Single(registry.Definitions, item =>
                item.Id == "future-harness");
            Assert.Equal("future-harness-micro-v1", future.PipeName);
            Assert.Same(future, registry.Resolve("future-harness"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PersistsLaunchSettingsPerHarness()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-settings-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            Assert.True(registry.UpdateConnectionSettings(
                "deepseek-harness",
                new(
                    "custom-deepseek-pipe",
                    "custom-node.exe",
                    "server --port 4090",
                    directory,
                    AutoStart: false,
                    ReadyTimeoutMilliseconds: 7_500,
                    ControlUri: "http://127.0.0.1:3080/__agentcontroller/micro/request")));

            var restored = new MicroHarnessRegistry(settingsPath: settingsPath)
                .Resolve("deepseek-harness");

            Assert.Equal("custom-deepseek-pipe", restored.PipeName);
            Assert.Equal("custom-node.exe", restored.Connection.Executable);
            Assert.Equal("server --port 4090", restored.Connection.Arguments);
            Assert.Equal(directory, restored.Connection.WorkingDirectory);
            Assert.False(restored.Connection.AutoStart);
            Assert.Equal(7_500, restored.Connection.ReadyTimeoutMilliseconds);
            Assert.Equal(
                "http://127.0.0.1:3080/__agentcontroller/micro/request",
                restored.ControlUri);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsStateOverLoopbackHttpWhenHarnessRunsInWsl()
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var controlUri =
            $"http://127.0.0.1:{port}/__agentcontroller/micro/request";
        var definition = new MicroHarnessDefinition(
            "wsl-harness",
            "WSL Harness",
            "Loopback HTTP adapter",
            null,
            true,
            new(
                "unreachable-pipe",
                null,
                null,
                null,
                false,
                ControlUri: controlUri));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            Assert.Equal(
                "/__agentcontroller/micro/request",
                context.Request.Url?.AbsolutePath);
            using var reader = new StreamReader(
                context.Request.InputStream,
                context.Request.ContentEncoding);
            var request = await reader.ReadToEndAsync();
            Assert.Contains("\"action\":\"state/read\"", request);
            const string response =
                "{\"success\":true,\"message\":\"wsl ready\",\"state\":{\"capabilities\":{\"sessionList\":true,\"sessionActivation\":true,\"knobSettings\":true,\"voiceInput\":true,\"actions\":[]},\"sessions\":[{\"id\":\"wsl-session\",\"displayTitle\":\"Linux session\",\"running\":false,\"updatedAt\":42}]}}";
            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        });

        var snapshot = await registry.ReadStateAsync(definition.Id);
        await serverTask;

        Assert.NotNull(snapshot);
        var session = Assert.Single(snapshot.Sessions);
        Assert.Equal("wsl-session", session.Id);
        Assert.Equal("Linux session", session.DisplayTitle);
    }

    [Fact]
    public void PersistsIndependentKeyMapsPerHarnessWithoutOverwritingLaunchSettings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-keymap-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            var first = new MicroHarnessDefinition(
                "first-harness",
                "First Harness",
                "First direct adapter",
                null,
                true,
                new("first-pipe", null, null, null, false));
            var second = new MicroHarnessDefinition(
                "second-harness",
                "Second Harness",
                "Second direct adapter",
                null,
                true,
                new("second-pipe", null, null, null, false));
            var registry = new MicroHarnessRegistry(
                [first, second],
                settingsPath: settingsPath);

            Assert.True(registry.UpdateConnectionSettings(
                first.Id,
                first.Connection with { Executable = "first.exe" }));
            Assert.True(registry.UpdateKeyMapping(
                first.Id,
                MicroHarnessControlIds.Action07,
                MicroHarnessActionIds.NextSession));
            Assert.True(registry.UpdateKeyMapping(
                second.Id,
                MicroHarnessControlIds.Action07,
                MicroHarnessActionIds.CancelTurn));

            var restored = new MicroHarnessRegistry(
                [first, second],
                settingsPath: settingsPath);
            Assert.Equal(
                MicroHarnessActionIds.NextSession,
                restored.ResolveKeyMap(first.Id)
                    .Resolve(MicroHarnessControlIds.Action07));
            Assert.Equal(
                MicroHarnessActionIds.CancelTurn,
                restored.ResolveKeyMap(second.Id)
                    .Resolve(MicroHarnessControlIds.Action07));
            Assert.Equal("first.exe", restored.Resolve(first.Id).Connection.Executable);
            Assert.Equal(
                MicroHarnessActionIds.NewSession,
                restored.ResolveKeyMap(second.Id)
                    .Resolve(MicroHarnessControlIds.Action06));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsTypedSessionsFromDirectAdapter()
    {
        var pipeName = $"codex-micro-harness-state-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));

        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(
                server,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            var request = await reader.ReadLineAsync();
            Assert.Contains("\"action\":\"state/read\"", request);
            await writer.WriteLineAsync(
                """
                {"success":true,"message":"ready","state":{"capabilities":{"sessionList":true,"sessionActivation":true,"knobSettings":false,"voiceInput":true,"actions":["session/new","session/fork","turn/cancel","composer/back"]},"components":{"adapter":"ready","browser":"connected","voiceSetup":"required","voiceRuntime":"starting","voiceMessage":"Loading model"},"navigationDepth":2,"currentSessionId":"newer","sessions":[{"id":"older","displayTitle":"Older","running":false,"updatedAt":10},{"id":"newer","displayTitle":"Newer","running":true,"updatedAt":20}]}}
                """);
        });

        var snapshot = await registry.ReadStateAsync("test-harness");
        await serverTask;

        Assert.NotNull(snapshot);
        Assert.Equal("newer", snapshot.CurrentSessionId);
        Assert.Equal(["newer", "older"], snapshot.Sessions.Select(item => item.Id));
        Assert.True(snapshot.Capabilities.SessionActivation);
        Assert.False(snapshot.Capabilities.KnobSettings);
        Assert.True(snapshot.Capabilities.VoiceInput);
        Assert.True(snapshot.Capabilities.Supports(
            MicroHarnessActionIds.NewSession));
        Assert.True(snapshot.Capabilities.Supports(
            MicroHarnessActionIds.ForkSession));
        Assert.True(snapshot.Capabilities.Supports(
            MicroHarnessActionIds.CancelTurn));
        Assert.True(snapshot.Capabilities.Supports(
            MicroHarnessActionIds.ComposerBack));
        Assert.Equal(2, snapshot.NavigationDepth);
        Assert.NotNull(snapshot.Components);
        Assert.Equal("connected", snapshot.Components.Browser);
        Assert.Equal("required", snapshot.Components.VoiceSetup);
        Assert.Equal("starting", snapshot.Components.VoiceRuntime);
        Assert.Equal("Loading model", snapshot.Components.VoiceMessage);
    }

    [Fact]
    public async Task ExecutesHarnessActionWithExactSessionAndNoHidFallback()
    {
        var pipeName = $"codex-micro-harness-action-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));

        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(
                server,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            var request = await reader.ReadLineAsync();
            Assert.Contains("\"action\":\"action/execute\"", request);
            Assert.Contains("\"actionId\":\"session/fork\"", request);
            Assert.Contains("\"sessionId\":\"session-7\"", request);
            await writer.WriteLineAsync(
                "{\"success\":true,\"message\":\"forked\"}");
        });

        var result = await registry.ExecuteActionAsync(
            "test-harness",
            MicroHarnessActionIds.ForkSession,
            "session-7");
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal("forked", result.Message);
    }

    [Fact]
    public async Task WaitsForBrowserAcknowledgementAfterThePipeHasConnected()
    {
        var pipeName = $"codex-micro-harness-delayed-action-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));

        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(
                server,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            _ = await reader.ReadLineAsync();
            await Task.Delay(900);
            await writer.WriteLineAsync(
                "{\"success\":true,\"message\":\"browser acknowledged\"}");
        });

        var result = await registry.ExecuteActionAsync(
            "test-harness",
            MicroHarnessActionIds.ComposerBack);
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal("browser acknowledged", result.Message);
    }

    [Fact]
    public async Task SendsOrderedVoicePressAndReleaseEdgesThroughTheAdapter()
    {
        var pipeName = $"codex-micro-harness-voice-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));
        var requests = new List<string>();
        var serverTask = Task.Run(async () =>
        {
            for (var index = 0; index < 2; index++)
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(
                    server,
                    Encoding.UTF8,
                    leaveOpen: true);
                await using var writer = new StreamWriter(
                    server,
                    new UTF8Encoding(false),
                    leaveOpen: true)
                {
                    AutoFlush = true,
                };
                requests.Add((await reader.ReadLineAsync())!);
                await writer.WriteLineAsync(
                    "{\"success\":true,\"message\":\"voice edge accepted\"}");
            }
        });

        var pressed = await registry.SetVoiceAsync(definition.Id, pressed: true);
        var released = await registry.SetVoiceAsync(definition.Id, pressed: false);
        await serverTask;

        Assert.True(pressed.Success);
        Assert.True(released.Success);
        Assert.Contains("\"action\":\"voice/start\"", requests[0]);
        Assert.Contains("\"action\":\"voice/stop\"", requests[1]);
    }

    [Fact]
    public async Task OpensPluginOwnedVoiceConfigurationThroughTheAdapter()
    {
        var pipeName = $"codex-micro-harness-voice-config-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));
        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(
                server,
                Encoding.UTF8,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                server,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            var request = await reader.ReadLineAsync();
            Assert.Contains("\"action\":\"voice/configure\"", request);
            await writer.WriteLineAsync(
                "{\"success\":true,\"message\":\"plugin settings opened\"}");
        });

        var result = await registry.ConfigureVoiceAsync(definition.Id);
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal("plugin settings opened", result.Message);
    }

    [Fact]
    public void VoiceDictationCanOnlyBeMappedToMicrophoneControls()
    {
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new("test-pipe", null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));

        Assert.False(registry.UpdateKeyMapping(
            definition.Id,
            MicroHarnessControlIds.Action06,
            MicroHarnessActionIds.VoiceDictation));
        Assert.Equal(
            MicroHarnessActionIds.VoiceDictation,
            registry.ResolveKeyMap(definition.Id)
                .Resolve(MicroHarnessControlIds.VoiceWide));
    }

    [Fact]
    public async Task PreservesStructuredOpeningStatusFromTheAdapter()
    {
        var pipeName = $"codex-micro-harness-status-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "test-harness",
            "Test Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));
        var progress = new List<MicroHarnessDispatchProgress>();
        var serverTask = Task.Run(async () =>
        {
            await using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(
                server,
                new UTF8Encoding(false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            _ = await reader.ReadLineAsync();
            await writer.WriteLineAsync(
                "{\"success\":true,\"message\":\"opening dedicated window\",\"status\":\"opening\",\"windowProcessId\":4321}");
        });

        var result = await registry.ActivateAsync(
            definition.Id,
            new InlineProgress<MicroHarnessDispatchProgress>(progress.Add));
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal(MicroHarnessDispatchStage.Opening, result.Stage);
        Assert.Equal(4321, result.WindowProcessId);
        Assert.Contains(progress, item =>
            item.Stage == MicroHarnessDispatchStage.Connecting);
    }
}
