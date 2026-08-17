using CodexMicro.Desktop.Services;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    public void MigratesOnlyTheFormerFixedDeepSeekLaunchDefault()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-deepseek-path-migration-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "Harnesses": {
                    "deepseek-harness": {
                      "PipeName": "deepseek-harness-micro-v1",
                      "Executable": "C:\\Windows\\System32\\wsl.exe",
                      "Arguments": "--distribution Ubuntu --exec bash \"/mnt/d/AgentController/micro-bridge/DeepSeekHarness/scripts/start-dsh-wsl.sh\"",
                      "WorkingDirectory": "C:\\Windows\\System32",
                      "AutoStart": true,
                      "ReadyTimeoutMilliseconds": 120000,
                      "ControlUri": "http://127.0.0.1:3080/__agentcontroller/micro/request"
                    }
                  },
                  "SetupCompletedHarnesses": ["deepseek-harness"]
                }
                """);

            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            var deepSeek = registry.Resolve("deepseek-harness");

            Assert.Null(deepSeek.Connection.Executable);
            Assert.Null(deepSeek.Connection.Arguments);
            Assert.Null(deepSeek.Connection.WorkingDirectory);
            Assert.False(deepSeek.Connection.AutoStart);
            Assert.False(registry.IsSetupCompleted("deepseek-harness"));
            var migrated = File.ReadAllText(settingsPath);
            Assert.DoesNotContain("AgentController", migrated);
            Assert.DoesNotContain("--distribution Ubuntu", migrated);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void KeepsAUserProvidedDeepSeekLaunchCommand()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-deepseek-custom-launch-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                settingsPath,
                """
                {
                  "Harnesses": {
                    "deepseek-harness": {
                      "PipeName": "deepseek-harness-micro-v1",
                      "Executable": "C:\\Tools\\launch-my-harness.exe",
                      "Arguments": "--port 3090",
                      "WorkingDirectory": "C:\\Harness",
                      "AutoStart": true,
                      "ReadyTimeoutMilliseconds": 90000,
                      "ControlUri": "http://127.0.0.1:3090/__agentcontroller/micro/request"
                    }
                  },
                  "SetupCompletedHarnesses": ["deepseek-harness"]
                }
                """);

            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            var deepSeek = registry.Resolve("deepseek-harness");

            Assert.Equal(
                @"C:\Tools\launch-my-harness.exe",
                deepSeek.Connection.Executable);
            Assert.Equal("--port 3090", deepSeek.Connection.Arguments);
            Assert.True(deepSeek.Connection.AutoStart);
            Assert.True(registry.IsSetupCompleted("deepseek-harness"));
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
                manifestDirectory: directory,
                settingsPath: Path.Combine(directory, "settings.json"));

            Assert.Equal("Codex", registry.Definitions[0].DisplayName);
            Assert.Contains(registry.Definitions, item =>
                item.Id == "deepseek-harness");
            var deepSeek = registry.Resolve("deepseek-harness");
            Assert.Null(deepSeek.ProjectPath);
            Assert.True(deepSeek.IsAvailable);
            Assert.Null(deepSeek.Connection.Executable);
            Assert.Null(deepSeek.Connection.Arguments);
            Assert.False(deepSeek.Connection.AutoStart);
            Assert.Equal(
                "http://127.0.0.1:3080/__agentcontroller/micro/request",
                deepSeek.ControlUri);
            Assert.Equal(300_000,
                deepSeek.Connection.ReadyTimeoutMilliseconds);
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

    [Theory]
    [InlineData(60_000)]
    [InlineData(120_000)]
    public void MigratesFormerDeepSeekColdStartTimeouts(int formerTimeout)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-timeout-migration-tests",
            Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "harness-settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            var registry = new MicroHarnessRegistry(settingsPath: settingsPath);
            var deepSeek = registry.Resolve("deepseek-harness");
            Assert.True(registry.UpdateConnectionSettings(
                deepSeek.Id,
                deepSeek.Connection with
                {
                    ReadyTimeoutMilliseconds = formerTimeout,
                }));

            var restored = new MicroHarnessRegistry(settingsPath: settingsPath);

            Assert.Equal(
                300_000,
                restored.Resolve("deepseek-harness")
                    .Connection.ReadyTimeoutMilliseconds);
            Assert.Contains(
                "300000",
                File.ReadAllText(settingsPath),
                StringComparison.Ordinal);
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
        Assert.Equal(MicroHarnessSessionStatus.Idle, session.Status);
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
                {"success":true,"message":"ready","state":{"capabilities":{"sessionList":true,"sessionActivation":true,"knobSettings":false,"voiceInput":true,"actions":["session/new","session/fork","turn/cancel","composer/back","composer/submit"]},"components":{"adapter":"ready","browser":"connected","currentModel":"DeepSeek-V4-Pro"},"navigationDepth":2,"currentSessionId":"waiting","sessions":[{"id":"legacy-idle","displayTitle":"Legacy idle","running":false,"updatedAt":10},{"id":"completed","displayTitle":"Completed","status":"completed","running":false,"updatedAt":20},{"id":"error","displayTitle":"Error","status":"error","running":false,"updatedAt":30},{"id":"running","displayTitle":"Running","status":"running","running":true,"updatedAt":40},{"id":"waiting","displayTitle":"Waiting","status":"waiting","running":false,"updatedAt":50}]}}
                """);
        });

        var snapshot = await registry.ReadStateAsync("test-harness");
        await serverTask;

        Assert.NotNull(snapshot);
        Assert.Equal("waiting", snapshot.CurrentSessionId);
        Assert.Equal(
            ["waiting", "running", "error", "completed", "legacy-idle"],
            snapshot.Sessions.Select(item => item.Id));
        Assert.Equal(
            [
                MicroHarnessSessionStatus.WaitingForInput,
                MicroHarnessSessionStatus.Running,
                MicroHarnessSessionStatus.Error,
                MicroHarnessSessionStatus.Completed,
                MicroHarnessSessionStatus.Idle,
            ],
            snapshot.Sessions.Select(item => item.Status));
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
        Assert.True(snapshot.Capabilities.Supports(
            MicroHarnessActionIds.ComposerSubmit));
        Assert.Equal(2, snapshot.NavigationDepth);
        Assert.NotNull(snapshot.Components);
        Assert.Equal("connected", snapshot.Components.Browser);
        Assert.Equal("DeepSeek-V4-Pro", snapshot.Components.CurrentModel);
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
    public async Task RelaysVoiceButtonStatusAndKeypadDictationThroughTheAdapter()
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
            for (var index = 0; index < 4; index++)
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
                var request = (await reader.ReadLineAsync())!;
                requests.Add(request);
                await writer.WriteLineAsync(index == 0
                    ? "{\"success\":true,\"message\":\"button ready\",\"voiceRequest\":{\"requestId\":\"voice-1\",\"command\":\"toggle\",\"sessionId\":\"session-1\"}}"
                    : "{\"success\":true,\"message\":\"relay accepted\"}");
            }
        });

        var voiceRequest = await registry.WaitForVoiceRequestAsync(
            definition.Id);
        var completed = await registry.CompleteVoiceRequestAsync(
            definition.Id,
            "voice-1",
            success: true,
            active: true,
            message: "The keypad microphone is listening.");
        var status = await registry.PublishVoiceStatusAsync(
            definition.Id,
            active: true,
            phase: "listening",
            message: "The keypad microphone is listening.",
            sessionId: "session-1");
        var dictation = await registry.SendDictationAsync(
            definition.Id,
            "你好",
            "zh-CN",
            autoSubmit: true,
            sessionId: "session-1",
            dictationId: "dictation-stream-1",
            dictationPhase: "final");
        await serverTask;

        Assert.NotNull(voiceRequest);
        Assert.Equal("voice-1", voiceRequest.RequestId);
        Assert.Equal("session-1", voiceRequest.SessionId);
        Assert.True(completed.Success);
        Assert.True(status.Success);
        Assert.True(dictation.Success);
        Assert.Contains("\"action\":\"voice/request\"", requests[0]);
        Assert.Contains("\"action\":\"voice/result\"", requests[1]);
        Assert.Contains("\"requestId\":\"voice-1\"", requests[1]);
        Assert.Contains("\"action\":\"voice/status\"", requests[2]);
        Assert.Contains("\"action\":\"composer/dictate\"", requests[3]);
        Assert.Contains("\"autoSubmit\":true", requests[3]);
        using var dictationRequest = JsonDocument.Parse(requests[3]);
        Assert.Equal(
            "你好",
            dictationRequest.RootElement.GetProperty("text").GetString());
        Assert.Equal(
            "dictation-stream-1",
            dictationRequest.RootElement.GetProperty("dictationId").GetString());
        Assert.Equal(
            "final",
            dictationRequest.RootElement.GetProperty("dictationPhase").GetString());
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

    [Fact]
    public async Task OneActivationWaitsForTheBrowserSurfaceWithoutASecondClick()
    {
        var pipeName = $"codex-micro-harness-ready-{Guid.NewGuid():N}";
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
            for (var attempt = 0; attempt < 2; attempt++)
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
                _ = await reader.ReadLineAsync();
                await writer.WriteLineAsync(attempt == 0
                    ? "{\"success\":true,\"message\":\"opening dedicated window\",\"status\":\"opening\"}"
                    : "{\"success\":true,\"message\":\"browser connected\",\"status\":\"background\"}");
            }
        });

        var result = await registry.ActivateUntilSurfaceReadyAsync(
            definition.Id,
            new InlineProgress<MicroHarnessDispatchProgress>(progress.Add));
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal(MicroHarnessDispatchStage.Background, result.Stage);
        Assert.Equal("browser connected", result.Message);
        Assert.Equal(5, result.Step);
        Assert.Equal(7, result.TotalSteps);
        Assert.Contains(progress, item =>
            item is { Step: 1, TotalSteps: 7 });
        Assert.Contains(progress, item =>
            item is { Step: 4, TotalSteps: 7 });
        Assert.Contains(progress, item =>
            item is { Step: 5, TotalSteps: 7 });
    }

    [Theory]
    [InlineData(false, 1, 6_789)]
    [InlineData(true, 0, 4_321)]
    public async Task DeepSeekActivationOwnsExactlyOneWindowsSurfaceLaunch(
        bool adapterAlreadyLaunched,
        int expectedLaunches,
        int expectedProcessId)
    {
        var pipeName = $"codex-micro-deepseek-surface-{Guid.NewGuid():N}";
        var definition = new MicroHarnessDefinition(
            "deepseek-harness",
            "DeepSeek Harness",
            "Test direct adapter",
            null,
            true,
            new(pipeName, null, null, null, false));
        var launches = new List<MicroHarnessDefinition>();
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"),
            surfaceLauncher: harness =>
            {
                launches.Add(harness);
                return new(true, 6_789, null);
            });
        var serverTask = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 2; attempt++)
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
                _ = await reader.ReadLineAsync();
                await writer.WriteLineAsync(attempt == 0
                    ? adapterAlreadyLaunched
                        ? "{\"success\":true,\"message\":\"opening dedicated window\",\"status\":\"opening\",\"windowProcessId\":4321}"
                        : "{\"success\":true,\"message\":\"opening dedicated window\",\"status\":\"opening\"}"
                    : "{\"success\":true,\"message\":\"dedicated surface connected\",\"status\":\"background\"}");
            }
        });

        var result = await registry.ActivateUntilSurfaceReadyAsync(definition.Id);
        await serverTask;

        Assert.True(result.Success, result.Message);
        Assert.Equal(MicroHarnessDispatchStage.Background, result.Stage);
        Assert.Equal(expectedProcessId, result.WindowProcessId);
        Assert.Equal(expectedLaunches, launches.Count);
        Assert.All(launches, launched =>
            Assert.Equal(definition.Id, launched.Id));
    }

    [Fact]
    public async Task ColdStartProbesReadinessBeforeDispatchingActivation()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-cold-start-probe-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            var requests = new List<string>();
            var serverTask = Task.Run(async () =>
            {
                // Let both pre-launch HTTP attempts exhaust the handler's
                // 650 ms loopback connection timeout before the adapter joins.
                await Task.Delay(1_500);
                listener.Start();
                for (var index = 0; index < 2; index++)
                {
                    var context = await listener.GetContextAsync()
                        .WaitAsync(TimeSpan.FromSeconds(5));
                    using var reader = new StreamReader(
                        context.Request.InputStream,
                        context.Request.ContentEncoding);
                    var request = await reader.ReadToEndAsync();
                    requests.Add(request);
                    var response = index == 0
                        ? "{\"success\":true,\"message\":\"adapter ready\",\"state\":{}}"
                        : "{\"success\":true,\"message\":\"activation dispatched once\",\"status\":\"background\"}";
                    var bytes = Encoding.UTF8.GetBytes(response);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            });
            var definition = new MicroHarnessDefinition(
                "cold-harness",
                "Cold Harness",
                "Delayed loopback adapter",
                null,
                true,
                new(
                    null,
                    Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    "/d /c exit 0",
                    directory,
                    true,
                    5_000,
                    $"http://127.0.0.1:{port}/__agentcontroller/micro/request"));
            var registry = new MicroHarnessRegistry(
                [definition],
                settingsPath: Path.Combine(directory, "harness-settings.json"));

            var result = await registry.ActivateAsync(definition.Id);
            await serverTask;

            Assert.True(result.Success, result.Message);
            Assert.Equal("activation dispatched once", result.Message);
            Assert.Equal(2, requests.Count);
            Assert.Contains("\"action\":\"state/read\"", requests[0]);
            Assert.Contains("\"action\":\"activate\"", requests[1]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PersistsTheLastColdStartTimeoutWithProbeAndProcessDetails()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-harness-timeout-diagnostic-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            var definition = new MicroHarnessDefinition(
                "timeout-harness",
                "Timeout Harness",
                "Offline loopback adapter",
                null,
                true,
                new(
                    null,
                    Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                    "/d /c exit 0",
                    directory,
                    true,
                    1_000,
                    $"http://127.0.0.1:{port}/__agentcontroller/micro/request"));
            var settingsPath = Path.Combine(directory, "harness-settings.json");
            var registry = new MicroHarnessRegistry(
                [definition],
                settingsPath: settingsPath);

            var result = await registry.ActivateAsync(definition.Id);

            Assert.False(result.Success);
            Assert.Contains("Last check:", result.Message);
            var diagnostic = registry.GetLastTimeoutDiagnostic(definition.Id);
            Assert.NotNull(diagnostic);
            Assert.Equal(1_000, diagnostic.ConfiguredTimeoutMilliseconds);
            Assert.True(diagnostic.ElapsedMilliseconds >= 1_000);
            Assert.True(diagnostic.ProbeAttempts >= 1);
            Assert.Contains("not connected", diagnostic.LastProbeMessage);
            Assert.NotNull(diagnostic.LauncherProcessId);
            Assert.Equal(false, diagnostic.LauncherWasRunning);
            Assert.Equal(0, diagnostic.LauncherExitCode);

            var restored = new MicroHarnessRegistry(
                [definition],
                settingsPath: settingsPath);
            Assert.Equal(
                diagnostic,
                restored.GetLastTimeoutDiagnostic(definition.Id));
            Assert.True(File.Exists(Path.Combine(
                directory,
                "harness-diagnostics.json")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SlowHttpResponseIsNotMistakenForAnOfflineAdapter()
    {
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            using var reader = new StreamReader(
                context.Request.InputStream,
                context.Request.ContentEncoding);
            _ = await reader.ReadToEndAsync();
            await Task.Delay(4_500);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(
                    "{\"success\":true,\"message\":\"late response\"}");
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (Exception exception) when (
                exception is HttpListenerException or IOException or
                    ObjectDisposedException)
            {
                // The client intentionally cancels before this late response.
            }
        });
        var definition = new MicroHarnessDefinition(
            "slow-http-harness",
            "Slow HTTP Harness",
            "Connected adapter with a delayed acknowledgement",
            null,
            true,
            new(
                null,
                "this-launcher-must-not-run.exe",
                null,
                null,
                true,
                1_000,
                $"http://127.0.0.1:{port}/__agentcontroller/micro/request"));
        var registry = new MicroHarnessRegistry(
            [definition],
            settingsPath: Path.Combine(
                Path.GetTempPath(),
                $"missing-{Guid.NewGuid():N}",
                "settings.json"));
        var progress = new List<MicroHarnessDispatchProgress>();

        var result = await registry.ActivateAsync(
            definition.Id,
            new InlineProgress<MicroHarnessDispatchProgress>(progress.Add));
        await serverTask;

        Assert.False(result.Success);
        Assert.Contains("response failed", result.Message);
        Assert.DoesNotContain(progress, item =>
            item.Stage == MicroHarnessDispatchStage.Starting);
    }
}
