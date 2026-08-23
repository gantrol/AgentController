using System.Diagnostics;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Agents.DeepSeek;

/// <summary>
/// Direct DeepSeek Harness adapter. All business actions go through the
/// versioned loopback bridge; no keyboard or pointer input is synthesized.
/// </summary>
public sealed class DeepSeekAgentTarget : IAgentTarget
{
    public static readonly AgentId DeepSeekId =
        new("deepseek-harness");

    private readonly object _stateGate = new();
    private DeepSeekHarnessState? _lastState;
    private string? _previousSessionId;

    public DeepSeekAgentTarget(DeepSeekHarnessClient client)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Presence = new PresenceAdapter(this);
        Shortcuts = new ShortcutAdapter(this);
        Workspace = new WorkspaceAdapter(this);
        Sidebar = new SidebarAdapter(this);
        Composer = new ComposerAdapter(this);
        DeepLinks = new DeepLinkAdapter(client.Endpoint);
    }

    public AgentId Id => DeepSeekId;

    public string DisplayName => "DeepSeek Harness";

    public AgentCapabilities Capabilities =>
        AgentCapabilities.Presence |
        AgentCapabilities.Shortcuts |
        AgentCapabilities.Workspace |
        AgentCapabilities.Sidebar |
        AgentCapabilities.Composer |
        AgentCapabilities.DeepLinks;

    public IAgentPresence Presence { get; }

    public IAgentShortcuts Shortcuts { get; }

    public IWorkspaceReader Workspace { get; }

    public ISidebarAutomation Sidebar { get; }

    public IComposerAutomation Composer { get; }

    public IDeepLinks DeepLinks { get; }

    public IKeybindingProvisioner? Keybindings => null;

    internal DeepSeekHarnessClient Client { get; }

    internal DeepSeekHarnessState? LastState
    {
        get
        {
            lock (_stateGate)
            {
                return _lastState;
            }
        }
    }

    internal async Task<DeepSeekHarnessResponse> RefreshStateAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await Client.ReadStateAsync(cancellationToken)
            .ConfigureAwait(false);
        if (response.State is not null)
        {
            RememberState(response.State);
        }

        return response;
    }

    internal async Task<DeepSeekHarnessResponse> ActivateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var before = LastState?.CurrentSessionId;
        var response = await Client
            .ActivateSessionAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (response.Success)
        {
            lock (_stateGate)
            {
                if (!string.IsNullOrWhiteSpace(before) &&
                    !string.Equals(
                        before,
                        sessionId,
                        StringComparison.Ordinal))
                {
                    _previousSessionId = before;
                }

                if (_lastState is not null)
                {
                    _lastState = _lastState with
                    {
                        CurrentSessionId = sessionId,
                    };
                }
            }
        }

        return response;
    }

    internal async Task<DeepSeekHarnessResponse> UndoSessionAsync(
        CancellationToken cancellationToken = default)
    {
        string? previous;
        lock (_stateGate)
        {
            previous = _previousSessionId;
        }

        if (string.IsNullOrWhiteSpace(previous))
        {
            return new(
                false,
                "No previous DeepSeek Harness session is available.",
                ErrorCode: "navigation.undo.unavailable");
        }

        return await ActivateSessionAsync(previous, cancellationToken)
            .ConfigureAwait(false);
    }

    internal string? CurrentSessionId => LastState?.CurrentSessionId;

    private void RememberState(DeepSeekHarnessState state)
    {
        lock (_stateGate)
        {
            _lastState = state;
        }
    }

    private DeepSeekHarnessState? ReadStateBlocking()
    {
        var response = RefreshStateAsync().GetAwaiter().GetResult();
        return response.State ?? LastState;
    }

    private sealed class PresenceAdapter : IAgentPresence
    {
        private readonly DeepSeekAgentTarget _owner;

        internal PresenceAdapter(DeepSeekAgentTarget owner)
        {
            _owner = owner;
        }

        public bool IsForeground => DeepSeekHarnessWindow.IsForeground();

        public bool Wake()
        {
            if (DeepSeekHarnessWindow.TryActivate())
            {
                return true;
            }

            var response = _owner.Client
                .ActivateAsync()
                .GetAwaiter()
                .GetResult();
            if (!response.Success)
            {
                return false;
            }

            var preferredProcessId = response.WindowProcessId;
            if (response.Status == "opening")
            {
                preferredProcessId ??=
                    DeepSeekHarnessWindow.TryLaunch(
                        _owner.Client.Endpoint);
            }

            var deadline = Environment.TickCount64 + 15_000;
            while (Environment.TickCount64 < deadline)
            {
                if (DeepSeekHarnessWindow.TryActivate(preferredProcessId))
                {
                    return true;
                }

                Thread.Sleep(120);
            }

            return DeepSeekHarnessWindow.TryActivate(preferredProcessId);
        }
    }

    private sealed class ShortcutAdapter : IAgentShortcuts
    {
        private readonly DeepSeekAgentTarget _owner;

        internal ShortcutAdapter(DeepSeekAgentTarget owner)
        {
            _owner = owner;
        }

        public bool CanExecute(AppSettings settings) =>
            settings.BridgeEnabled &&
            (!settings.OnlyWhenCodexForeground ||
                _owner.Presence.IsForeground);

        public bool Execute(string shortcut, AppSettings settings) => false;

        public async Task<bool> StepModelAsync(
            int steps,
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (steps == 0 || !CanExecute(settings))
            {
                return false;
            }

            var response = await _owner.Client.ExecuteActionAsync(
                    "model/toggle-quick",
                    _owner.CurrentSessionId,
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Success;
        }
    }

    private sealed class WorkspaceAdapter : IWorkspaceReader
    {
        private readonly DeepSeekAgentTarget _owner;

        internal WorkspaceAdapter(DeepSeekAgentTarget owner)
        {
            _owner = owner;
        }

        public CodexSnapshot LoadSnapshot()
        {
            var state = _owner.ReadStateBlocking();
            var threads = state?.Sessions
                .Select(ToThread)
                .ToArray() ?? [];
            return new()
            {
                Threads = threads,
                ProjectlessThreads = threads,
            };
        }

        public bool IsThreadAvailable(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return false;
            }

            var state = _owner.LastState;
            return state?.Sessions.Any(session => string.Equals(
                session.Id,
                threadId,
                StringComparison.Ordinal)) == true;
        }

        public IReadOnlyList<SidebarEntry> BuildEntries(
            CodexSnapshot snapshot,
            SidebarScope scope,
            string? selectedProjectPath) =>
            scope is SidebarScope.ProjectlessTasks or SidebarScope.PinnedTasks
                ? BuildUnifiedEntries(snapshot)
                : [];

        public IReadOnlyList<SidebarEntry> BuildUnifiedEntries(
            CodexSnapshot snapshot) => snapshot.Threads
            .Select(thread => new SidebarEntry(
                thread.Id,
                thread.Title,
                StatusLabel(thread.Status),
                SidebarLayer.Tasks,
                ThreadId: thread.Id,
                NativeTitle: thread.NativeTitle ?? thread.Title,
                NavigationScope: SidebarScope.ProjectlessTasks))
            .ToArray();

        private static CodexThread ToThread(DeepSeekHarnessSession session)
        {
            DateTimeOffset updatedAt;
            try
            {
                updatedAt = session.UpdatedAt > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(
                        session.UpdatedAt)
                    : DateTimeOffset.MinValue;
            }
            catch (ArgumentOutOfRangeException)
            {
                updatedAt = DateTimeOffset.MinValue;
            }

            return new(
                session.Id,
                session.DisplayTitle,
                updatedAt,
                ProjectPath: null,
                IsPinned: false,
                NativeTitle: session.DisplayTitle,
                Status: session.Status switch
                {
                    DeepSeekSessionStatus.Running => ThreadStatus.Thinking,
                    DeepSeekSessionStatus.Completed =>
                        ThreadStatus.CompleteUnread,
                    DeepSeekSessionStatus.WaitingForInput =>
                        ThreadStatus.RequiresInput,
                    DeepSeekSessionStatus.Error => ThreadStatus.Error,
                    _ => ThreadStatus.Idle,
                });
        }

        private static string StatusLabel(ThreadStatus status) =>
            status switch
            {
                ThreadStatus.Thinking => "Running",
                ThreadStatus.CompleteUnread => "Completed",
                ThreadStatus.RequiresInput => "Needs input",
                ThreadStatus.Error => "Error",
                _ => "Idle",
            };
    }

    private sealed class SidebarAdapter : ISidebarAutomation
    {
        private readonly DeepSeekAgentTarget _owner;

        internal SidebarAdapter(DeepSeekAgentTarget owner)
        {
            _owner = owner;
        }

        public string? TryGetCurrentThreadTitle()
        {
            var state = _owner.LastState;
            return state?.Sessions.FirstOrDefault(session =>
                string.Equals(
                    session.Id,
                    state.CurrentSessionId,
                    StringComparison.Ordinal))?.DisplayTitle;
        }

        public SidebarAutomationResult RestoreDisclosure(
            ProjectDisclosureLease lease) =>
            new(false, AgentAutomationErrorCodes.CapabilityUnavailable);

        public int? TryGetBottomTaskCount() =>
            _owner.LastState?.Sessions.Count;
    }

    private sealed class ComposerAdapter : IComposerAutomation
    {
        private readonly DeepSeekAgentTarget _owner;

        internal ComposerAdapter(DeepSeekAgentTarget owner)
        {
            _owner = owner;
        }

        public ComposerCatalog LoadCatalog()
        {
            var state = _owner.LastState;
            var model = state?.CurrentModel;
            return new()
            {
                Models = string.IsNullOrWhiteSpace(model)
                    ? []
                    : [new ComposerModelOption(model, model, [])],
                InitialModelIndex = 0,
                InitialEffort = string.Empty,
                InitialSpeed = string.Empty,
            };
        }

        public Task<ComposerAutomationResult> SelectAsync(
            ComposerSettingKind kind,
            string target,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(Unavailable());

        public async Task<ComposerPickerResult> OpenPickerAsync(
            ComposerPickerView view,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            var result = await ExecuteAsync(
                    "composer/activate-selection",
                    cancellationToken)
                .ConfigureAwait(false);
            return new(
                result.Succeeded,
                IsMenuOpen: result.Succeeded,
                Error: result.Error,
                ErrorDetail: result.ErrorDetail);
        }

        public async Task<ComposerPickerResult> StepSimplePowerAsync(
            int steps,
            bool allowShortcutFastPath,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            var result = await ExecuteAsync(
                    steps < 0
                        ? "reasoning/decrease"
                        : "reasoning/increase",
                    cancellationToken)
                .ConfigureAwait(false);
            return Picker(result);
        }

        public async Task<ComposerPickerResult> SetSimpleSpeedAsync(
            bool fast,
            bool allowShortcutFastPath,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            Picker(await ExecuteAsync(
                    "model/toggle-quick",
                    cancellationToken)
                .ConfigureAwait(false));

        public Task<ComposerPickerResult> ToggleSpeedAsync(
            bool allowShortcutFastPath,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            SetSimpleSpeedAsync(
                true,
                allowShortcutFastPath,
                settings,
                cancellationToken);

        public Task<ComposerPickerResult> StepAdvancedAsync(
            ComposerSettingKind kind,
            int direction,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            kind == ComposerSettingKind.Effort
                ? StepSimplePowerAsync(
                    direction,
                    allowShortcutFastPath: false,
                    settings,
                    cancellationToken)
                : ToggleSpeedAsync(
                    allowShortcutFastPath: false,
                    settings,
                    cancellationToken);

        public Task<ComposerPlanToggleResult> TogglePlanModeAsync(
            string shortcut,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ComposerPlanToggleResult(
                false,
                Error: AgentAutomationErrorCodes.CapabilityUnavailable));

        public string? TryReadComposerButtonName() =>
            _owner.LastState?.CurrentModel;

        public string? TryReadDispatchButtonName() => "Send";

        public bool IsActionAvailable(params string[] actionNames) => false;

        public ComposerAutomationResult InvokeAction(
            AppSettings settings,
            params string[] actionNames) => Unavailable();

        public Task<ComposerAutomationResult> InvokeActionAsync(
            AppSettings settings,
            int timeoutMs,
            CancellationToken cancellationToken,
            params string[] actionNames) =>
            Task.FromResult(Unavailable());

        public ComposerDialResult ProbeDialState()
        {
            var state = _owner.ReadStateBlocking();
            return state is null
                ? DialFailure("deepseek.harness.offline")
                : new(
                    true,
                    state.CurrentModel ?? "DeepSeek Harness",
                    IsMenuOpen: state.NavigationDepth > 0,
                    MenuWasPresent: state.NavigationDepth > 0,
                    StateVerified: true);
        }

        public ComposerDialResult DialStep(
            int delta,
            AppSettings settings) =>
            DialAction(delta < 0
                ? "composer/select-previous"
                : "composer/select-next");

        public ComposerDialResult DialNavigate(
            ComposerDialNavigation navigation,
            AppSettings settings) =>
            DialStep(
                navigation is ComposerDialNavigation.Left or
                    ComposerDialNavigation.Up
                    ? -1
                    : 1,
                settings);

        public ComposerDialResult DialPress(AppSettings settings) =>
            DialAction("composer/activate-selection");

        public ComposerDialResult DialSelect(AppSettings settings) =>
            DialPress(settings);

        public ComposerDialResult DialCancel(
            AppSettings settings,
            bool menuExpected = false) =>
            DialAction("composer/back");

        public ComposerAutomationResult Cancel(AppSettings settings) =>
            ExecuteBlocking("composer/back");

        private ComposerDialResult DialAction(string actionId)
        {
            var result = ExecuteBlocking(actionId);
            if (!result.Succeeded)
            {
                return DialFailure(
                    result.Error ?? "deepseek.harness.rejected",
                    result.ErrorDetail);
            }

            var state = _owner.ReadStateBlocking();
            return new(
                true,
                state?.CurrentModel ?? "DeepSeek Harness",
                IsMenuOpen: state?.NavigationDepth > 0,
                MenuWasPresent: state?.NavigationDepth > 0,
                StateVerified: state is not null);
        }

        private ComposerAutomationResult ExecuteBlocking(string actionId) =>
            ExecuteAsync(actionId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

        private async Task<ComposerAutomationResult> ExecuteAsync(
            string actionId,
            CancellationToken cancellationToken)
        {
            var response = await _owner.Client.ExecuteActionAsync(
                    actionId,
                    _owner.CurrentSessionId,
                    cancellationToken)
                .ConfigureAwait(false);
            return response.Success
                ? new(
                    true,
                    Channel: ComposerAutomationChannel.Unknown,
                    StateVerified: response.WasDispatched)
                : new(
                    false,
                    response.ErrorCode ?? "deepseek.harness.rejected",
                    response.Message);
        }

        private static ComposerPickerResult Picker(
            ComposerAutomationResult result) =>
            new(
                result.Succeeded,
                Error: result.Error,
                ErrorDetail: result.ErrorDetail);

        private static ComposerAutomationResult Unavailable() =>
            new(false, AgentAutomationErrorCodes.CapabilityUnavailable);

        private static ComposerDialResult DialFailure(
            string error,
            string? detail = null) =>
            new(
                false,
                "DeepSeek Harness",
                Error: error,
                ErrorDetail: detail,
                StateVerified: false);
    }

    private sealed class DeepLinkAdapter : IDeepLinks
    {
        private readonly Uri _surface;

        internal DeepLinkAdapter(Uri endpoint)
        {
            _surface = new UriBuilder(endpoint)
            {
                Path = "/",
                Query = string.Empty,
                Fragment = string.Empty,
            }.Uri;
        }

        public void OpenSettings() => Open();

        public void OpenKeyboardShortcuts() => Open();

        private void Open()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _surface.AbsoluteUri,
                UseShellExecute = true,
            });
        }
    }
}
