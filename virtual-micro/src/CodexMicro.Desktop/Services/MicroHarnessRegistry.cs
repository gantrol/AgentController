using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record MicroHarnessConnectionSettings(
    string? PipeName,
    string? Executable,
    string? Arguments,
    string? WorkingDirectory,
    bool AutoStart,
    int ReadyTimeoutMilliseconds = 60_000,
    string? ControlUri = null);

internal sealed record MicroHarnessDefinition(
    string Id,
    string DisplayName,
    string Description,
    string? ProjectPath,
    bool IsAvailable,
    MicroHarnessConnectionSettings Connection)
{
    internal string? PipeName => Connection.PipeName;
    internal string? ControlUri => Connection.ControlUri;

    public override string ToString() => DisplayName;
}

internal enum MicroHarnessDispatchStage
{
    Connecting,
    Starting,
    WaitingForAdapter,
    Opening,
    Foreground,
    Background,
    Completed,
    Failed,
}

internal sealed record MicroHarnessDispatchProgress(
    MicroHarnessDispatchStage Stage,
    string Message,
    int? Step = null,
    int? TotalSteps = null);

internal sealed record MicroHarnessDispatchResult(
    bool Success,
    string Message,
    MicroHarnessDispatchStage Stage,
    int? WindowProcessId = null,
    int? Step = null,
    int? TotalSteps = null);

internal sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}

internal static class MicroHarnessActionIds
{
    internal const string None = "none";
    internal const string NewSession = "session/new";
    internal const string ForkSession = "session/fork";
    internal const string ArchiveSession = "session/archive";
    internal const string CancelTurn = "turn/cancel";
    internal const string ToggleConversationView =
        "view/toggle-chat-trajectory";
    internal const string ApproveInteraction = "interaction/approve";
    internal const string RejectInteraction = "interaction/reject";
    internal const string LoadOlderHistory = "history/load-older";
    internal const string ToggleSidebar = "layout/toggle-sidebar";
    internal const string OpenDetails = "layout/open-details";
    internal const string CloseDetails = "layout/close-details";
    internal const string PreviousSession = "session/previous";
    internal const string NextSession = "session/next";
    internal const string OpenSelectedSession = "session/open-selected";
    internal const string ActivateSurface = "surface/activate";
    internal const string VoiceDictation = "voice/dictation";
    internal const string ComposerSelectPrevious = "composer/select-previous";
    internal const string ComposerSelectNext = "composer/select-next";
    internal const string ComposerActivateSelection =
        "composer/activate-selection";
    internal const string ComposerBack = "composer/back";
    internal const string ComposerSubmit = "composer/submit";
    internal const string ReasoningDecrease = "reasoning/decrease";
    internal const string ReasoningIncrease = "reasoning/increase";
    internal const string ToggleQuickModel = "model/toggle-quick";
    internal const string OpenGoal = "goal/open";

    internal static readonly IReadOnlyList<string> Configurable =
    [
        None,
        NewSession,
        ToggleConversationView,
        ApproveInteraction,
        CancelTurn,
        ForkSession,
        RejectInteraction,
        ArchiveSession,
        LoadOlderHistory,
        ToggleSidebar,
        OpenDetails,
        CloseDetails,
        PreviousSession,
        NextSession,
        OpenSelectedSession,
        ActivateSurface,
        VoiceDictation,
        OpenGoal,
    ];

    internal static bool IsNative(string actionId) =>
        actionId is NewSession or
            ToggleConversationView or
            ForkSession or
            ArchiveSession or
            CancelTurn or
            ApproveInteraction or
            RejectInteraction or
            LoadOlderHistory or
            ToggleSidebar or
            OpenDetails or
            CloseDetails or
            ComposerSelectPrevious or
            ComposerSelectNext or
            ComposerActivateSelection or
            ComposerBack or
            ComposerSubmit or
            ReasoningDecrease or
            ReasoningIncrease or
            ToggleQuickModel or
            OpenGoal;

    internal static bool IsVoice(string actionId) =>
        actionId == VoiceDictation;
}

internal static class MicroHarnessKnobModes
{
    internal const string ComposerNavigation = "composer-navigation";
    internal const string ReasoningOnly = "reasoning";
    // Read-only migration alias from the first external-Harness prototype.
    internal const string QuickActions = "quick-actions";
    internal const string RecentSessions = "recent-sessions";

    internal static readonly IReadOnlyList<string> Configurable =
    [
        ComposerNavigation,
        ReasoningOnly,
        RecentSessions,
    ];
}

internal static class MicroHarnessControlIds
{
    internal const string Action06 = "ACT06";
    internal const string Action07 = "ACT07";
    internal const string Action08 = "ACT08";
    internal const string Action09 = "ACT09";
    internal const string VoiceWide = "ACT10_ACT11";
    internal const string VoiceLeft = "ACT10";
    internal const string VoiceRight = "ACT11";
    internal const string JoystickUp = "JOY_UP";
    internal const string JoystickDown = "JOY_DOWN";
    internal const string JoystickLeft = "JOY_LEFT";
    internal const string JoystickRight = "JOY_RIGHT";

    internal static readonly IReadOnlyList<string> All =
    [
        Action06,
        Action07,
        Action08,
        Action09,
        VoiceWide,
        VoiceLeft,
        VoiceRight,
        JoystickUp,
        JoystickDown,
        JoystickLeft,
        JoystickRight,
    ];

    internal static bool IsVoice(string controlId) =>
        controlId is VoiceWide or VoiceLeft or VoiceRight;
}

internal sealed record MicroHarnessKeyMap(
    IReadOnlyDictionary<string, string> Bindings)
{
    internal string Resolve(string controlId) =>
        Bindings.TryGetValue(controlId, out var actionId)
            ? actionId
            : MicroHarnessActionIds.None;
}

internal sealed record MicroHarnessCapabilities(
    bool SessionList,
    bool SessionActivation,
    bool KnobSettings,
    bool VoiceInput,
    IReadOnlySet<string> Actions)
{
    internal bool Supports(string actionId) => Actions.Contains(actionId);
}

internal sealed record MicroHarnessSession(
    string Id,
    string DisplayTitle,
    bool Running,
    long UpdatedAt);

internal sealed record MicroHarnessComponentSnapshot(
    string Adapter,
    string Browser,
    string? CurrentModel = null);

internal sealed record MicroHarnessVoiceRequest(
    string RequestId,
    string? SessionId);

internal sealed record MicroHarnessStateSnapshot(
    string HarnessId,
    IReadOnlyList<MicroHarnessSession> Sessions,
    string? CurrentSessionId,
    MicroHarnessCapabilities Capabilities,
    int NavigationDepth,
    MicroHarnessComponentSnapshot? Components,
    DateTimeOffset ReadAt);

/// <summary>
/// Discovers direct Micro adapters. External Harnesses register a manifest and
/// expose a versioned local endpoint; no keyboard or pointer simulation is used.
/// The registry also owns per-adapter launch settings so an activation can
/// start an offline Harness and wait for its endpoint to become ready.
/// </summary>
internal sealed class MicroHarnessRegistry
{
    private const int ConnectTimeoutMilliseconds = 650;
    private const int RetryConnectTimeoutMilliseconds = 350;
    private const int ResponseTimeoutMilliseconds = 4_000;
    private const int SurfaceReadyTimeoutMilliseconds = 20_000;
    private const int SurfaceRetryIntervalMilliseconds = 750;
    private const int DeepSeekReadyTimeoutMilliseconds = 120_000;
    private const int LegacyDeepSeekReadyTimeoutMilliseconds = 60_000;
    internal const string DeepSeekOfficialWebUri =
        "http://127.0.0.1:3080/";
    internal const string DeepSeekControlUri =
        "http://127.0.0.1:3080/__agentcontroller/micro/request";
    private static readonly TimeSpan ReadyPollInterval =
        TimeSpan.FromMilliseconds(250);
    private static readonly HttpClient LoopbackControlClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromMilliseconds(
                ConnectTimeoutMilliseconds),
        })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly string? _settingsPath;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private IReadOnlyList<MicroHarnessDefinition> _definitions;
    private Dictionary<string, MicroHarnessKeyMap> _keyMaps;
    private readonly Dictionary<string, string> _knobModes;
    private readonly HashSet<string> _setupCompleted;

    internal MicroHarnessRegistry(
        IEnumerable<MicroHarnessDefinition>? definitions = null,
        string? manifestDirectory = null,
        string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
        _definitions = definitions is null
            ? Discover(manifestDirectory)
            : Normalize(definitions);
        var stored = ReadOverrides(_settingsPath);
        var deepSeekDefault = _definitions.FirstOrDefault(item =>
            string.Equals(
                item.Id,
                "deepseek-harness",
                StringComparison.OrdinalIgnoreCase));
        var migrateLegacyFixedDeepSeekLaunch =
            deepSeekDefault is not null &&
            stored.Harnesses?.TryGetValue(
                "deepseek-harness",
                out var legacyDeepSeek) == true &&
            IsLegacyFixedDeepSeekLaunch(legacyDeepSeek);
        if (migrateLegacyFixedDeepSeekLaunch)
        {
            stored.Harnesses!["deepseek-harness"] =
                deepSeekDefault!.Connection;
            stored.SetupCompletedHarnesses =
                (stored.SetupCompletedHarnesses ?? [])
                    .Where(id => !string.Equals(
                        id,
                        "deepseek-harness",
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
        }
        var migrateDeepSeekReadyTimeout =
            deepSeekDefault?.Connection.ReadyTimeoutMilliseconds ==
                DeepSeekReadyTimeoutMilliseconds &&
            stored.Harnesses?.TryGetValue(
                "deepseek-harness",
                out var storedDeepSeek) == true &&
            storedDeepSeek.ReadyTimeoutMilliseconds ==
                LegacyDeepSeekReadyTimeoutMilliseconds;
        _definitions = ApplyOverrides(
            _definitions,
            stored.Harnesses ??
                new Dictionary<string, MicroHarnessConnectionSettings>());
        if (migrateDeepSeekReadyTimeout)
        {
            _definitions = _definitions.Select(item =>
                string.Equals(
                    item.Id,
                    "deepseek-harness",
                    StringComparison.OrdinalIgnoreCase)
                        ? item with
                        {
                            Connection = item.Connection with
                            {
                                ReadyTimeoutMilliseconds =
                                    DeepSeekReadyTimeoutMilliseconds,
                            },
                        }
                        : item).ToArray();
        }
        _setupCompleted = new HashSet<string>(
            stored.SetupCompletedHarnesses ?? [],
            StringComparer.OrdinalIgnoreCase);
        var migrateLegacyDefaults = stored.KeyMaps?.Any(item =>
            IsLegacyDefaultKeyMap(item.Value) ||
            UsesPreviousJoystickDefaults(item.Value)) == true;
        var migrateLegacyKnobModes = stored.KnobModes?.Values.Any(mode =>
            string.Equals(
                mode,
                MicroHarnessKnobModes.QuickActions,
                StringComparison.Ordinal)) == true;
        _keyMaps = InitializeKeyMaps(_definitions, stored.KeyMaps);
        _knobModes = InitializeKnobModes(_definitions, stored.KnobModes);
        if (migrateLegacyDefaults || migrateLegacyKnobModes ||
            migrateLegacyFixedDeepSeekLaunch ||
            migrateDeepSeekReadyTimeout)
        {
            LastSaveSucceeded = PersistOverrides();
        }
    }

    internal event EventHandler? Changed;

    internal IReadOnlyList<MicroHarnessDefinition> Definitions => _definitions;

    internal bool LastSaveSucceeded { get; private set; } = true;

    internal bool IsSetupCompleted(string harnessId) =>
        _setupCompleted.Contains(Resolve(harnessId).Id);

    internal bool MarkSetupCompleted(string harnessId)
    {
        var harness = Resolve(harnessId);
        if (harness.Id == "codex")
        {
            return false;
        }

        var changed = _setupCompleted.Add(harness.Id);
        LastSaveSucceeded = PersistOverrides();
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return LastSaveSucceeded;
    }

    internal MicroHarnessDefinition Resolve(string? id) =>
        _definitions.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ??
        _definitions[0];

    internal MicroHarnessKeyMap ResolveKeyMap(string harnessId)
    {
        var harness = Resolve(harnessId);
        return _keyMaps.TryGetValue(harness.Id, out var map)
            ? map
            : CreateDefaultKeyMap();
    }

    internal string ResolveKnobMode(string harnessId)
    {
        var harness = Resolve(harnessId);
        return _knobModes.TryGetValue(harness.Id, out var mode)
            ? mode
            : MicroHarnessKnobModes.ComposerNavigation;
    }

    internal bool UpdateKnobMode(string harnessId, string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(harnessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        var harness = Resolve(harnessId);
        if (harness.Id == "codex" ||
            !MicroHarnessKnobModes.Configurable.Contains(
                mode,
                StringComparer.Ordinal))
        {
            return false;
        }

        if (string.Equals(
            ResolveKnobMode(harness.Id),
            mode,
            StringComparison.Ordinal))
        {
            LastSaveSucceeded = PersistOverrides();
            return LastSaveSucceeded;
        }

        _knobModes[harness.Id] = mode;
        LastSaveSucceeded = PersistOverrides();
        Changed?.Invoke(this, EventArgs.Empty);
        return LastSaveSucceeded;
    }

    internal bool ResetKeyMap(string harnessId)
    {
        var harness = Resolve(harnessId);
        if (harness.Id == "codex")
        {
            return false;
        }

        _keyMaps[harness.Id] = CreateDefaultKeyMap();
        LastSaveSucceeded = PersistOverrides();
        Changed?.Invoke(this, EventArgs.Empty);
        return LastSaveSucceeded;
    }

    internal bool UpdateKeyMapping(
        string harnessId,
        string controlId,
        string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(harnessId);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var harness = Resolve(harnessId);
        if (harness.Id == "codex" ||
            !MicroHarnessControlIds.All.Contains(controlId, StringComparer.Ordinal) ||
            !MicroHarnessActionIds.Configurable.Contains(actionId, StringComparer.Ordinal) ||
            (MicroHarnessActionIds.IsVoice(actionId) &&
                !MicroHarnessControlIds.IsVoice(controlId)))
        {
            return false;
        }

        var current = ResolveKeyMap(harness.Id);
        if (string.Equals(
            current.Resolve(controlId),
            actionId,
            StringComparison.Ordinal))
        {
            LastSaveSucceeded = PersistOverrides();
            return LastSaveSucceeded;
        }

        var bindings = new Dictionary<string, string>(
            current.Bindings,
            StringComparer.Ordinal)
        {
            [controlId] = actionId,
        };
        _keyMaps[harness.Id] = new MicroHarnessKeyMap(bindings);
        LastSaveSucceeded = PersistOverrides();
        Changed?.Invoke(this, EventArgs.Empty);
        return LastSaveSucceeded;
    }

    internal bool UpdateConnectionSettings(
        string harnessId,
        MicroHarnessConnectionSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(harnessId);
        var harness = Resolve(harnessId);
        if (harness.Id == "codex")
        {
            return false;
        }

        var normalized = NormalizeConnection(settings, harness.ProjectPath);
        if (normalized == harness.Connection)
        {
            // Retry an earlier failed atomic write even when the controls have
            // not changed; otherwise the UI could report "saved" while the
            // last durable state is still stale.
            LastSaveSucceeded = PersistOverrides();
            return LastSaveSucceeded;
        }

        _definitions = _definitions
            .Select(item => item.Id == harness.Id
                ? item with
                {
                    Connection = normalized,
                    IsAvailable = IsDefinitionAvailable(
                        item.ProjectPath,
                        normalized),
                }
                : item)
            .ToArray();
        LastSaveSucceeded = PersistOverrides();
        Changed?.Invoke(this, EventArgs.Empty);
        return LastSaveSucceeded;
    }

    internal Task<MicroHarnessDispatchResult> ActivateAsync(
        string harnessId,
        IProgress<MicroHarnessDispatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        DispatchWithOptionalLaunchAsync(
            Resolve(harnessId),
            new { version = 1, source = "codex-micro", action = "activate" },
            progress,
            cancellationToken);

    /// <summary>
    /// Keeps one user activation alive until the browser surface has joined
    /// the adapter. The activate request is intentionally idempotent, so a
    /// cold or lost Chromium launch can be retried without duplicating a
    /// session or any other user action.
    /// </summary>
    internal async Task<MicroHarnessDispatchResult>
        ActivateUntilSurfaceReadyAsync(
            string harnessId,
            IProgress<MicroHarnessDispatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
    {
        var harness = Resolve(harnessId);
        var latestStep = 1;
        void Report(
            MicroHarnessDispatchProgress value,
            int step)
        {
            latestStep = Math.Max(latestStep, step);
            progress?.Report(value with
            {
                Step = step,
                TotalSteps = 7,
            });
        }

        var activationProgress = new CallbackProgress<
            MicroHarnessDispatchProgress>(value =>
        {
            var step = value.Stage switch
            {
                MicroHarnessDispatchStage.Starting => 2,
                MicroHarnessDispatchStage.WaitingForAdapter => 3,
                _ => 1,
            };
            Report(value, step);
        });
        var request = new
        {
            version = 1,
            source = "codex-micro",
            action = "activate",
        };
        var result = await DispatchWithOptionalLaunchAsync(
            harness,
            request,
            activationProgress,
            cancellationToken);
        if (!result.Success)
        {
            return result with
            {
                Step = latestStep,
                TotalSteps = 7,
            };
        }

        Report(new(
            MicroHarnessDispatchStage.Opening,
            $"Requested {harness.DisplayName} browser surface."), 4);
        if (result.Stage != MicroHarnessDispatchStage.Opening)
        {
            Report(new(
                MicroHarnessDispatchStage.Background,
                $"{harness.DisplayName} browser bridge is connected."), 5);
            return result with { Step = 5, TotalSteps = 7 };
        }

        Report(new(
            MicroHarnessDispatchStage.Opening,
            $"Waiting for {harness.DisplayName} browser surface."), 5);
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(
            SurfaceReadyTimeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(
                SurfaceRetryIntervalMilliseconds,
                cancellationToken);
            var exchange = await ExchangeAsync(
                harness,
                request,
                RetryConnectTimeoutMilliseconds,
                cancellationToken);
            if (!exchange.Connected)
            {
                continue;
            }

            result = ToDispatchResult(exchange);
            if (!result.Success ||
                result.Stage != MicroHarnessDispatchStage.Opening)
            {
                if (result.Success)
                {
                    Report(new(
                        MicroHarnessDispatchStage.Background,
                        $"{harness.DisplayName} browser bridge is connected."), 5);
                }
                return result with { Step = 5, TotalSteps = 7 };
            }
        }

        return result with { Step = 5, TotalSteps = 7 };
    }

    internal Task<MicroHarnessDispatchResult> ActivateSessionAsync(
        string harnessId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return DispatchWithOptionalLaunchAsync(
            Resolve(harnessId),
            new
            {
                version = 1,
                source = "codex-micro",
                action = "session/activate",
                sessionId,
            },
            progress: null,
            cancellationToken);
    }

    internal Task<MicroHarnessDispatchResult> ExecuteActionAsync(
        string harnessId,
        string actionId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        object request = string.IsNullOrWhiteSpace(sessionId)
            ? new
            {
                version = 1,
                source = "codex-micro",
                action = "action/execute",
                actionId,
            }
            : new
            {
                version = 1,
                source = "codex-micro",
                action = "action/execute",
                actionId,
                sessionId,
            };
        return DispatchWithOptionalLaunchAsync(
            Resolve(harnessId),
            request,
            progress: null,
            cancellationToken);
    }

    internal async Task<MicroHarnessVoiceRequest?> WaitForVoiceRequestAsync(
        string harnessId,
        CancellationToken cancellationToken = default)
    {
        var harness = Resolve(harnessId);
        if (harness.Id == "codex" ||
            !harness.IsAvailable ||
            !HasControlEndpoint(harness.Connection))
        {
            return null;
        }

        var exchange = await ExchangeAsync(
            harness,
            new
            {
                version = 1,
                source = "codex-micro",
                action = "voice/request",
            },
            ConnectTimeoutMilliseconds,
            cancellationToken,
            responseTimeoutMilliseconds: 25_000);
        if (!exchange.Connected || !exchange.Success ||
            exchange.Root.ValueKind != JsonValueKind.Object ||
            !exchange.Root.TryGetProperty("voiceRequest", out var request) ||
            request.ValueKind != JsonValueKind.Object ||
            !TryReadString(request, "requestId", out var requestId))
        {
            return null;
        }

        return new(
            requestId,
            TryReadString(request, "sessionId", out var sessionId)
                ? sessionId
                : null);
    }

    internal Task<MicroHarnessDispatchResult> CompleteVoiceRequestAsync(
        string harnessId,
        string requestId,
        bool success,
        bool active,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        return DispatchWithoutLaunchAsync(
            Resolve(harnessId),
            new
            {
                version = 1,
                source = "codex-micro",
                action = "voice/result",
                requestId,
                success,
                active,
                message,
            },
            cancellationToken);
    }

    internal Task<MicroHarnessDispatchResult> PublishVoiceStatusAsync(
        string harnessId,
        bool active,
        string phase,
        string message,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        object request = string.IsNullOrWhiteSpace(sessionId)
            ? new
            {
                version = 1,
                source = "codex-micro",
                action = "voice/status",
                active,
                phase,
                message,
            }
            : new
            {
                version = 1,
                source = "codex-micro",
                action = "voice/status",
                active,
                phase,
                message,
                sessionId,
            };
        return DispatchWithoutLaunchAsync(
            Resolve(harnessId),
            request,
            cancellationToken);
    }

    internal Task<MicroHarnessDispatchResult> SendDictationAsync(
        string harnessId,
        string text,
        string language,
        bool autoSubmit,
        string? sessionId = null,
        CancellationToken cancellationToken = default,
        string? dictationId = null,
        string? dictationPhase = null)
    {
        if (dictationPhase is not (null or "partial" or "final" or "cancel"))
        {
            throw new ArgumentOutOfRangeException(nameof(dictationPhase));
        }
        if (string.IsNullOrWhiteSpace(text) && dictationPhase != "cancel")
        {
            throw new ArgumentException(
                "Dictation text is required unless a live preview is being cancelled.",
                nameof(text));
        }
        if (dictationPhase is not null && string.IsNullOrWhiteSpace(dictationId))
        {
            throw new ArgumentException(
                "Live dictation requires a stable id.",
                nameof(dictationId));
        }

        var request = new Dictionary<string, object?>
        {
            ["version"] = 1,
            ["source"] = "codex-micro",
            ["action"] = "composer/dictate",
            ["text"] = text,
            ["language"] = language,
            ["autoSubmit"] = autoSubmit,
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request["sessionId"] = sessionId;
        }
        if (!string.IsNullOrWhiteSpace(dictationId))
        {
            request["dictationId"] = dictationId;
        }
        if (dictationPhase is not null)
        {
            request["dictationPhase"] = dictationPhase;
        }
        return DispatchWithOptionalLaunchAsync(
            Resolve(harnessId),
            request,
            progress: null,
            cancellationToken,
            responseTimeoutMilliseconds: 8_000);
    }

    private async Task<MicroHarnessDispatchResult> DispatchWithoutLaunchAsync(
        MicroHarnessDefinition harness,
        object request,
        CancellationToken cancellationToken)
    {
        if (harness.Id == "codex" ||
            !harness.IsAvailable ||
            !HasControlEndpoint(harness.Connection))
        {
            return new(
                false,
                $"{harness.DisplayName} adapter is not connected.",
                MicroHarnessDispatchStage.Failed);
        }

        var exchange = await ExchangeAsync(
            harness,
            request,
            ConnectTimeoutMilliseconds,
            cancellationToken,
            ResponseTimeoutMilliseconds);
        return exchange.Connected
            ? ToDispatchResult(exchange)
            : new(
                false,
                exchange.Message,
                MicroHarnessDispatchStage.Failed);
    }

    internal async Task<MicroHarnessStateSnapshot?> ReadStateAsync(
        string harnessId,
        CancellationToken cancellationToken = default)
    {
        var harness = Resolve(harnessId);
        if (harness.Id == "codex" ||
            !harness.IsAvailable ||
            !HasControlEndpoint(harness.Connection))
        {
            return null;
        }

        var exchange = await ExchangeAsync(
            harness,
            new { version = 1, source = "codex-micro", action = "state/read" },
            ConnectTimeoutMilliseconds,
            cancellationToken);
        if (!exchange.Connected || !exchange.Success ||
            exchange.Root.ValueKind != JsonValueKind.Object ||
            !exchange.Root.TryGetProperty("state", out var state) ||
            state.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var sessions = new List<MicroHarnessSession>();
        if (state.TryGetProperty("sessions", out var sessionValues) &&
            sessionValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in sessionValues.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object ||
                    !TryReadString(value, "id", out var id))
                {
                    continue;
                }

                var title = TryReadString(value, "displayTitle", out var displayTitle)
                    ? displayTitle
                    : id;
                var running = value.TryGetProperty("running", out var runningValue) &&
                    runningValue.ValueKind == JsonValueKind.True;
                var updatedAt = value.TryGetProperty("updatedAt", out var updatedAtValue) &&
                    updatedAtValue.TryGetInt64(out var timestamp)
                        ? timestamp
                        : 0;
                sessions.Add(new(id, title, running, updatedAt));
            }
        }

        string? currentSessionId = null;
        if (TryReadString(state, "currentSessionId", out var current))
        {
            currentSessionId = current;
        }

        var navigationDepth = state.TryGetProperty(
                "navigationDepth",
                out var navigationDepthValue) &&
            navigationDepthValue.TryGetInt32(out var parsedNavigationDepth)
                ? Math.Max(0, parsedNavigationDepth)
                : 0;

        var capabilities = new MicroHarnessCapabilities(
            SessionList: true,
            SessionActivation: true,
            KnobSettings: false,
            VoiceInput: false,
            Actions: new HashSet<string>(StringComparer.Ordinal));
        if (state.TryGetProperty("capabilities", out var capabilityValues) &&
            capabilityValues.ValueKind == JsonValueKind.Object)
        {
            var actions = new HashSet<string>(StringComparer.Ordinal);
            if (capabilityValues.TryGetProperty("actions", out var actionValues) &&
                actionValues.ValueKind == JsonValueKind.Array)
            {
                foreach (var actionValue in actionValues.EnumerateArray())
                {
                    if (actionValue.ValueKind == JsonValueKind.String &&
                        actionValue.GetString() is { Length: > 0 } actionId)
                    {
                        actions.Add(actionId);
                    }
                }
            }

            capabilities = new(
                ReadBoolean(capabilityValues, "sessionList"),
                ReadBoolean(capabilityValues, "sessionActivation"),
                ReadBoolean(capabilityValues, "knobSettings"),
                ReadBoolean(capabilityValues, "voiceInput"),
                actions);
        }

        MicroHarnessComponentSnapshot? components = null;
        if (state.TryGetProperty("components", out var componentValues) &&
            componentValues.ValueKind == JsonValueKind.Object &&
            TryReadString(componentValues, "adapter", out var adapter) &&
            TryReadString(componentValues, "browser", out var browser))
        {
            components = new(
                adapter,
                browser,
                TryReadString(componentValues, "currentModel", out var currentModel)
                    ? currentModel
                    : null);
        }

        return new(
            harness.Id,
            sessions
                .OrderByDescending(item => item.UpdatedAt)
                .Take(6)
                .ToArray(),
            currentSessionId,
            capabilities,
            navigationDepth,
            components,
            DateTimeOffset.Now);
    }

    private async Task<MicroHarnessDispatchResult> DispatchWithOptionalLaunchAsync(
        MicroHarnessDefinition harness,
        object request,
        IProgress<MicroHarnessDispatchProgress>? progress,
        CancellationToken cancellationToken,
        int responseTimeoutMilliseconds = ResponseTimeoutMilliseconds)
    {
        progress?.Report(new(
            MicroHarnessDispatchStage.Connecting,
            $"Connecting to {harness.DisplayName}."));
        if (harness.Id == "codex")
        {
            return new(
                true,
                "Codex uses the native Micro HID route.",
                MicroHarnessDispatchStage.Completed);
        }

        if (!harness.IsAvailable || !HasControlEndpoint(harness.Connection))
        {
            return new(
                false,
                $"{harness.DisplayName} adapter is not configured.",
                MicroHarnessDispatchStage.Failed);
        }

        var first = await ExchangeAsync(
            harness,
            request,
            ConnectTimeoutMilliseconds,
            cancellationToken,
            responseTimeoutMilliseconds);
        if (first.Connected)
        {
            return ToDispatchResult(first);
        }

        if (!harness.Connection.AutoStart ||
            string.IsNullOrWhiteSpace(harness.Connection.Executable))
        {
            return new(
                false,
                $"{harness.DisplayName} is offline and automatic startup is disabled.",
                MicroHarnessDispatchStage.Failed);
        }

        await _launchGate.WaitAsync(cancellationToken);
        try
        {
            // Another click may have completed startup while this one waited.
            var retry = await ExchangeAsync(
                harness,
                request,
                RetryConnectTimeoutMilliseconds,
                cancellationToken,
                responseTimeoutMilliseconds);
            if (retry.Connected)
            {
                return ToDispatchResult(retry);
            }

            progress?.Report(new(
                MicroHarnessDispatchStage.Starting,
                $"Starting {harness.DisplayName}."));
            var launchError = TryStart(harness);
            if (launchError is not null)
            {
                return new(
                    false,
                    launchError,
                    MicroHarnessDispatchStage.Failed);
            }

            progress?.Report(new(
                MicroHarnessDispatchStage.WaitingForAdapter,
                $"Waiting for {harness.DisplayName} adapter."));
            var deadline = DateTimeOffset.UtcNow.AddMilliseconds(
                harness.Connection.ReadyTimeoutMilliseconds);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(ReadyPollInterval, cancellationToken);
                var ready = await ExchangeAsync(
                    harness,
                    request,
                    RetryConnectTimeoutMilliseconds,
                    cancellationToken,
                    responseTimeoutMilliseconds);
                if (ready.Connected)
                {
                    return ToDispatchResult(ready);
                }
            }

            return new(
                false,
                $"{harness.DisplayName} was started but its adapter did not become ready in time.",
                MicroHarnessDispatchStage.Failed);
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private static MicroHarnessDispatchResult ToDispatchResult(
        PipeExchange exchange)
    {
        var stage = !exchange.Success
            ? MicroHarnessDispatchStage.Failed
            : exchange.Status switch
            {
                "opening" => MicroHarnessDispatchStage.Opening,
                "foreground" => MicroHarnessDispatchStage.Foreground,
                "background" => MicroHarnessDispatchStage.Background,
                _ => MicroHarnessDispatchStage.Completed,
            };
        return new(
            exchange.Success,
            exchange.Message,
            stage,
            exchange.WindowProcessId);
    }

    private static string? TryStart(MicroHarnessDefinition harness)
    {
        try
        {
            var connection = harness.Connection;
            var startInfo = new ProcessStartInfo
            {
                FileName = connection.Executable!,
                Arguments = connection.Arguments ?? string.Empty,
                WorkingDirectory = connection.WorkingDirectory ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(startInfo);
            return process is null
                ? $"{harness.DisplayName} could not be started."
                : null;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            return $"{harness.DisplayName} could not be started ({exception.GetType().Name}).";
        }
    }

    private static async Task<PipeExchange> ExchangeAsync(
        MicroHarnessDefinition harness,
        object request,
        int connectTimeoutMilliseconds,
        CancellationToken cancellationToken,
        int responseTimeoutMilliseconds = ResponseTimeoutMilliseconds)
    {
        if (!string.IsNullOrWhiteSpace(harness.ControlUri))
        {
            return await ExchangeHttpAsync(
                harness,
                request,
                connectTimeoutMilliseconds,
                cancellationToken,
                responseTimeoutMilliseconds);
        }

        var connected = false;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            connectTimeout.CancelAfter(connectTimeoutMilliseconds);
            await using var pipe = new NamedPipeClientStream(
                ".",
                harness.PipeName!,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(connectTimeout.Token);
            connected = true;

            // Connecting to the local adapter and waiting for a browser action
            // acknowledgement are different phases. A slow browser must not be
            // mistaken for an offline adapter, which would otherwise trigger a
            // duplicate auto-start after the action had already executed.
            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            responseTimeout.CancelAfter(responseTimeoutMilliseconds);

            var requestJson = JsonSerializer.Serialize(request);
            await using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await writer.WriteLineAsync(requestJson.AsMemory(), responseTimeout.Token);
            var response = await reader.ReadLineAsync(responseTimeout.Token);
            if (string.IsNullOrWhiteSpace(response))
            {
                return new(true, false,
                    $"{harness.DisplayName} adapter returned no response.",
                    default);
            }

            return ParseExchangeResponse(harness, response);
        }
        catch (Exception exception) when (
            exception is IOException or
                OperationCanceledException or
                UnauthorizedAccessException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(connected, false,
                connected
                    ? $"{harness.DisplayName} adapter response failed ({exception.GetType().Name})."
                    : $"{harness.DisplayName} adapter is not connected ({exception.GetType().Name}).",
                default);
        }
        catch (JsonException)
        {
            return new(true, false,
                $"{harness.DisplayName} adapter returned malformed JSON.",
                default);
        }
    }

    private static async Task<PipeExchange> ExchangeHttpAsync(
        MicroHarnessDefinition harness,
        object request,
        int connectTimeoutMilliseconds,
        CancellationToken cancellationToken,
        int responseTimeoutMilliseconds)
    {
        _ = connectTimeoutMilliseconds;
        var connected = false;
        try
        {
            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            responseTimeout.CancelAfter(responseTimeoutMilliseconds);
            var requestJson = JsonSerializer.Serialize(request);
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                harness.ControlUri)
            {
                Content = new StringContent(
                    requestJson,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    "application/json"),
            };
            using var response = await LoopbackControlClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                responseTimeout.Token);
            connected = true;
            var body = await response.Content.ReadAsStringAsync(responseTimeout.Token);
            if (string.IsNullOrWhiteSpace(body))
            {
                return new(true, false,
                    $"{harness.DisplayName} adapter returned no response.",
                    default);
            }

            return ParseExchangeResponse(harness, body);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                OperationCanceledException or
                IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(connected, false,
                connected
                    ? $"{harness.DisplayName} adapter response failed ({exception.GetType().Name})."
                    : $"{harness.DisplayName} adapter is not connected ({exception.GetType().Name}).",
                default);
        }
        catch (JsonException)
        {
            return new(true, false,
                $"{harness.DisplayName} adapter returned malformed JSON.",
                default);
        }
    }

    private static PipeExchange ParseExchangeResponse(
        MicroHarnessDefinition harness,
        string response)
    {
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;
        var success = root.TryGetProperty("success", out var successValue) &&
            successValue.ValueKind is JsonValueKind.True;
        var message = root.TryGetProperty("message", out var messageValue) &&
            messageValue.ValueKind == JsonValueKind.String
                ? messageValue.GetString()
                : null;
        var status = root.TryGetProperty("status", out var statusValue) &&
            statusValue.ValueKind == JsonValueKind.String
                ? statusValue.GetString()
                : null;
        var windowProcessId = root.TryGetProperty(
                "windowProcessId",
                out var processIdValue) &&
            processIdValue.TryGetInt32(out var processId)
                ? processId
                : (int?)null;
        return new(
            true,
            success,
            message ?? (success
                ? $"Activated {harness.DisplayName}."
                : $"{harness.DisplayName} rejected the request."),
            root.Clone(),
            status,
            windowProcessId);
    }

    private static IReadOnlyList<MicroHarnessDefinition> Discover(
        string? manifestDirectory)
    {
        var definitions = new List<MicroHarnessDefinition>
        {
            CodexDefinition(),
        };

        var deepSeekConnection = new MicroHarnessConnectionSettings(
            "deepseek-harness-micro-v1",
            Executable: null,
            Arguments: null,
            WorkingDirectory: null,
            AutoStart: false,
            ReadyTimeoutMilliseconds: DeepSeekReadyTimeoutMilliseconds,
            ControlUri: DeepSeekControlUri);
        definitions.Add(new(
            "deepseek-harness",
            "DeepSeek Harness",
            "Direct plugin adapter · no simulated input",
            ProjectPath: null,
            IsAvailable: true,
            deepSeekConnection));

        manifestDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexMicro",
            "harnesses");
        if (Directory.Exists(manifestDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(
                         manifestDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<HarnessManifest>(
                        File.ReadAllText(path),
                        JsonOptions());
                    if (manifest is null ||
                        string.IsNullOrWhiteSpace(manifest.Id) ||
                        string.IsNullOrWhiteSpace(manifest.DisplayName) ||
                        (string.IsNullOrWhiteSpace(manifest.PipeName) &&
                            string.IsNullOrWhiteSpace(manifest.ControlUri)))
                    {
                        continue;
                    }

                    var connection = NormalizeConnection(new(
                        manifest.PipeName,
                        manifest.Executable,
                        manifest.Arguments,
                        manifest.WorkingDirectory ?? manifest.ProjectPath,
                        manifest.AutoStart,
                        manifest.ReadyTimeoutMilliseconds,
                        manifest.ControlUri),
                        manifest.ProjectPath);
                    definitions.Add(new(
                        manifest.Id.Trim(),
                        manifest.DisplayName.Trim(),
                        manifest.Description?.Trim() ?? "Direct Harness adapter",
                        manifest.ProjectPath,
                        IsDefinitionAvailable(manifest.ProjectPath, connection),
                        connection));
                }
                catch (Exception exception) when (
                    exception is IOException or
                        UnauthorizedAccessException or
                        JsonException)
                {
                    // A malformed optional manifest cannot hide built-in targets.
                }
            }
        }

        return Normalize(definitions);
    }

    private static IReadOnlyList<MicroHarnessDefinition> Normalize(
        IEnumerable<MicroHarnessDefinition> definitions)
    {
        var unique = new Dictionary<string, MicroHarnessDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            unique.TryAdd(definition.Id, definition);
        }

        if (!unique.ContainsKey("codex"))
        {
            unique["codex"] = CodexDefinition();
        }

        return [
            unique["codex"],
            .. unique.Values
                .Where(item => item.Id != "codex")
                .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase),
        ];
    }

    private static MicroHarnessDefinition CodexDefinition() => new(
        "codex",
        "Codex",
        "Native Codex Micro HID and task routing",
        null,
        true,
        new(null, null, null, null, false, 0));

    private static MicroHarnessConnectionSettings NormalizeConnection(
        MicroHarnessConnectionSettings value,
        string? projectPath)
    {
        var timeout = Math.Clamp(value.ReadyTimeoutMilliseconds, 1_000, 120_000);
        return value with
        {
            PipeName = NormalizeText(value.PipeName),
            Executable = NormalizeText(value.Executable),
            Arguments = value.Arguments?.Trim(),
            WorkingDirectory = NormalizeText(value.WorkingDirectory) ??
                NormalizeText(projectPath),
            ReadyTimeoutMilliseconds = timeout,
            ControlUri = NormalizeControlUri(value.ControlUri),
        };
    }

    private static bool IsDefinitionAvailable(
        string? projectPath,
        MicroHarnessConnectionSettings connection)
    {
        // A user-provided working directory intentionally overrides the
        // manifest's discovery hint, so moving a Harness checkout does not
        // permanently disable its menu entry.
        var effectiveDirectory = NormalizeText(connection.WorkingDirectory) ??
            NormalizeText(projectPath);
        return (effectiveDirectory is null || Directory.Exists(effectiveDirectory)) &&
            HasControlEndpoint(connection);
    }

    private static bool HasControlEndpoint(MicroHarnessConnectionSettings connection) =>
        !string.IsNullOrWhiteSpace(connection.PipeName) ||
        !string.IsNullOrWhiteSpace(connection.ControlUri);

    private static string? NormalizeControlUri(string? value)
    {
        var text = NormalizeText(value);
        if (text is null ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !uri.IsLoopback ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsLegacyFixedDeepSeekLaunch(
        MicroHarnessConnectionSettings value)
    {
        var arguments = value.Arguments ?? string.Empty;
        return value.AutoStart &&
            arguments.Contains(
                "--distribution Ubuntu",
                StringComparison.OrdinalIgnoreCase) &&
            arguments.Contains(
                "start-dsh-wsl.sh",
                StringComparison.OrdinalIgnoreCase) &&
            ((arguments.Contains(
                    "/mnt/",
                    StringComparison.OrdinalIgnoreCase) &&
                arguments.Contains(
                    "/AgentController/",
                    StringComparison.OrdinalIgnoreCase)) ||
                arguments.Contains(
                    @":\AgentController\",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<MicroHarnessDefinition> ApplyOverrides(
        IReadOnlyList<MicroHarnessDefinition> definitions,
        IReadOnlyDictionary<string, MicroHarnessConnectionSettings> overrides) =>
        definitions.Select(definition =>
        {
            if (definition.Id == "codex" ||
                !overrides.TryGetValue(definition.Id, out var value))
            {
                return definition;
            }

            var connection = NormalizeConnection(value, definition.ProjectPath);
            return definition with
            {
                Connection = connection,
                IsAvailable = IsDefinitionAvailable(definition.ProjectPath, connection),
            };
        }).ToArray();

    private bool PersistOverrides()
    {
        if (_settingsPath is null)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var value = new StoredOverrides
            {
                Harnesses = _definitions
                    .Where(item => item.Id != "codex")
                    .ToDictionary(
                        item => item.Id,
                        item => item.Connection,
                        StringComparer.OrdinalIgnoreCase),
                KeyMaps = _keyMaps.ToDictionary(
                    item => item.Key,
                    item => new Dictionary<string, string>(
                        item.Value.Bindings,
                        StringComparer.Ordinal),
                    StringComparer.OrdinalIgnoreCase),
                KnobModes = new Dictionary<string, string>(
                    _knobModes,
                    StringComparer.OrdinalIgnoreCase),
                SetupCompletedHarnesses = _setupCompleted
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(value, new JsonSerializerOptions
                {
                    WriteIndented = true,
                }));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return false;
        }
    }

    private static StoredOverrides ReadOverrides(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return new StoredOverrides();
        }

        try
        {
            return JsonSerializer.Deserialize<StoredOverrides>(
                File.ReadAllText(path),
                JsonOptions()) ?? new StoredOverrides();
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return new StoredOverrides();
        }
    }

    private static Dictionary<string, MicroHarnessKeyMap> InitializeKeyMaps(
        IReadOnlyList<MicroHarnessDefinition> definitions,
        IReadOnlyDictionary<string, Dictionary<string, string>>? stored)
    {
        var result = new Dictionary<string, MicroHarnessKeyMap>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var harness in definitions.Where(item => item.Id != "codex"))
        {
            var bindings = new Dictionary<string, string>(
                CreateDefaultKeyMap().Bindings,
                StringComparer.Ordinal);
            if (stored is not null &&
                stored.TryGetValue(harness.Id, out var configured))
            {
                if (!IsLegacyDefaultKeyMap(configured))
                {
                    foreach (var (controlId, actionId) in configured)
                    {
                        if (MicroHarnessControlIds.All.Contains(
                                controlId,
                                StringComparer.Ordinal) &&
                            MicroHarnessActionIds.Configurable.Contains(
                                actionId,
                                StringComparer.Ordinal))
                        {
                            bindings[controlId] = actionId;
                        }
                    }
                }

                if (UsesPreviousJoystickDefaults(configured))
                {
                    var defaults = CreateDefaultKeyMap();
                    foreach (var controlId in new[]
                             {
                                 MicroHarnessControlIds.JoystickUp,
                                 MicroHarnessControlIds.JoystickDown,
                                 MicroHarnessControlIds.JoystickLeft,
                                 MicroHarnessControlIds.JoystickRight,
                             })
                    {
                        bindings[controlId] = defaults.Resolve(controlId);
                    }
                }
            }

            result[harness.Id] = new MicroHarnessKeyMap(bindings);
        }

        return result;
    }

    private static Dictionary<string, string> InitializeKnobModes(
        IReadOnlyList<MicroHarnessDefinition> definitions,
        IReadOnlyDictionary<string, string>? stored)
    {
        var result = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var harness in definitions.Where(item => item.Id != "codex"))
        {
            string? configured = null;
            if (stored is not null)
            {
                stored.TryGetValue(harness.Id, out configured);
            }
            var mode = string.Equals(
                configured,
                MicroHarnessKnobModes.QuickActions,
                StringComparison.Ordinal)
                    ? MicroHarnessKnobModes.ComposerNavigation
                    : configured is not null &&
                        MicroHarnessKnobModes.Configurable.Contains(
                            configured,
                            StringComparer.Ordinal)
                        ? configured
                        : MicroHarnessKnobModes.ComposerNavigation;
            result[harness.Id] = mode;
        }

        return result;
    }

    private static MicroHarnessKeyMap CreateDefaultKeyMap() => new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MicroHarnessControlIds.Action06] =
                MicroHarnessActionIds.NewSession,
            [MicroHarnessControlIds.Action07] =
                MicroHarnessActionIds.ToggleConversationView,
            [MicroHarnessControlIds.Action08] =
                MicroHarnessActionIds.CancelTurn,
            [MicroHarnessControlIds.Action09] =
                MicroHarnessActionIds.ForkSession,
            [MicroHarnessControlIds.VoiceWide] =
                MicroHarnessActionIds.VoiceDictation,
            [MicroHarnessControlIds.VoiceLeft] =
                MicroHarnessActionIds.VoiceDictation,
            [MicroHarnessControlIds.VoiceRight] =
                MicroHarnessActionIds.VoiceDictation,
            [MicroHarnessControlIds.JoystickUp] =
                MicroHarnessActionIds.PreviousSession,
            [MicroHarnessControlIds.JoystickDown] =
                MicroHarnessActionIds.NextSession,
            [MicroHarnessControlIds.JoystickLeft] =
                MicroHarnessActionIds.ToggleSidebar,
            [MicroHarnessControlIds.JoystickRight] =
                MicroHarnessActionIds.OpenDetails,
        });

    private static bool UsesPreviousJoystickDefaults(
        IReadOnlyDictionary<string, string> bindings) =>
        bindings.TryGetValue(
            MicroHarnessControlIds.JoystickUp,
            out var up) &&
        string.Equals(
            up,
            MicroHarnessActionIds.NewSession,
            StringComparison.Ordinal) &&
        bindings.TryGetValue(
            MicroHarnessControlIds.JoystickDown,
            out var down) &&
        string.Equals(
            down,
            MicroHarnessActionIds.CancelTurn,
            StringComparison.Ordinal) &&
        bindings.TryGetValue(
            MicroHarnessControlIds.JoystickLeft,
            out var left) &&
        string.Equals(
            left,
            MicroHarnessActionIds.PreviousSession,
            StringComparison.Ordinal) &&
        bindings.TryGetValue(
            MicroHarnessControlIds.JoystickRight,
            out var right) &&
        string.Equals(
            right,
            MicroHarnessActionIds.NextSession,
            StringComparison.Ordinal);

    private static bool IsLegacyDefaultKeyMap(
        IReadOnlyDictionary<string, string> bindings)
    {
        var oldest = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MicroHarnessControlIds.Action06] =
                MicroHarnessActionIds.NewSession,
            [MicroHarnessControlIds.Action07] = MicroHarnessActionIds.None,
            [MicroHarnessControlIds.Action08] = MicroHarnessActionIds.None,
            [MicroHarnessControlIds.Action09] =
                MicroHarnessActionIds.ForkSession,
            [MicroHarnessControlIds.VoiceWide] = MicroHarnessActionIds.None,
            [MicroHarnessControlIds.VoiceLeft] = MicroHarnessActionIds.None,
            [MicroHarnessControlIds.VoiceRight] = MicroHarnessActionIds.None,
            [MicroHarnessControlIds.JoystickUp] =
                MicroHarnessActionIds.NewSession,
            [MicroHarnessControlIds.JoystickDown] =
                MicroHarnessActionIds.CancelTurn,
            [MicroHarnessControlIds.JoystickLeft] =
                MicroHarnessActionIds.PreviousSession,
            [MicroHarnessControlIds.JoystickRight] =
                MicroHarnessActionIds.NextSession,
        };
        var previous = new Dictionary<string, string>(oldest, StringComparer.Ordinal)
        {
            [MicroHarnessControlIds.Action07] =
                MicroHarnessActionIds.ApproveInteraction,
            [MicroHarnessControlIds.Action08] =
                MicroHarnessActionIds.CancelTurn,
        };
        var latest = new Dictionary<string, string>(previous, StringComparer.Ordinal)
        {
            [MicroHarnessControlIds.VoiceWide] =
                MicroHarnessActionIds.VoiceDictation,
            [MicroHarnessControlIds.VoiceLeft] =
                MicroHarnessActionIds.VoiceDictation,
            [MicroHarnessControlIds.VoiceRight] =
                MicroHarnessActionIds.VoiceDictation,
        };
        return Matches(oldest) || Matches(previous) || Matches(latest);

        bool Matches(IReadOnlyDictionary<string, string> expected) =>
            bindings.Count == expected.Count &&
            expected.All(item =>
                bindings.TryGetValue(item.Key, out var value) &&
                string.Equals(value, item.Value, StringComparison.Ordinal));
    }

    private static bool TryReadString(
        JsonElement value,
        string name,
        out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        result = text;
        return true;
    }

    private static bool ReadBoolean(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.True;

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string GetDefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CodexMicro", "harness-settings.json");
    }

    private readonly record struct PipeExchange(
        bool Connected,
        bool Success,
        string Message,
        JsonElement Root,
        string? Status = null,
        int? WindowProcessId = null);

    private sealed class HarnessManifest
    {
        public string Id { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string PipeName { get; set; } = string.Empty;

        public string? ProjectPath { get; set; }

        public string? Executable { get; set; }

        public string? Arguments { get; set; }

        public string? ControlUri { get; set; }

        public string? WorkingDirectory { get; set; }

        public bool AutoStart { get; set; }

        public int ReadyTimeoutMilliseconds { get; set; } = 60_000;
    }

    private sealed class StoredOverrides
    {
        public Dictionary<string, MicroHarnessConnectionSettings>? Harnesses
        {
            get;
            set;
        }

        public Dictionary<string, Dictionary<string, string>>? KeyMaps
        {
            get;
            set;
        }

        public Dictionary<string, string>? KnobModes { get; set; }

        public string[]? SetupCompletedHarnesses { get; set; }
    }
}
