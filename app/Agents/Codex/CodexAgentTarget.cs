using CodexController.Models;
using CodexController.Services;

namespace CodexController.Agents.Codex;

/// <summary>
/// Ports-and-adapters seam over the existing Codex services. This deliberately
/// wraps those services instead of moving or rewriting them, allowing callers
/// to migrate one capability at a time.
/// </summary>
public sealed class CodexAgentTarget : IAgentTarget
{
    public static readonly AgentId CodexId = new("codex");

    public CodexAgentTarget(
        CodexCommandService commands,
        CodexDataService? workspace = null,
        CodexSidebarService? sidebar = null,
        CodexComposerService? composer = null,
        CodexKeybindingService? keybindings = null)
    {
        ArgumentNullException.ThrowIfNull(commands);

        Presence = new PresenceAdapter(commands);
        Shortcuts = new ShortcutAdapter(commands);
        Workspace = workspace is null
            ? null
            : new WorkspaceAdapter(workspace);
        Sidebar = sidebar is null
            ? null
            : new SidebarAdapter(sidebar);
        Composer = composer is null
            ? null
            : new ComposerAdapter(composer);
        DeepLinks = new DeepLinkAdapter();
        Keybindings = keybindings is null
            ? null
            : new KeybindingAdapter(keybindings);
        Capabilities = CalculateCapabilities(this);
    }

    public AgentId Id => CodexId;

    public string DisplayName => "Codex";

    public AgentCapabilities Capabilities { get; }

    public IAgentPresence Presence { get; }

    public IAgentShortcuts Shortcuts { get; }

    public IWorkspaceReader? Workspace { get; }

    public ISidebarAutomation? Sidebar { get; }

    public IComposerAutomation? Composer { get; }

    public IDeepLinks? DeepLinks { get; }

    public IKeybindingProvisioner? Keybindings { get; }

    public static CodexAgentTarget CreateDefault()
    {
        return new CodexAgentTarget(
            new CodexCommandService(),
            new CodexDataService(),
            new CodexSidebarService(),
            new CodexComposerService(),
            new CodexKeybindingService());
    }

    private static AgentCapabilities CalculateCapabilities(
        IAgentTarget target)
    {
        var capabilities =
            AgentCapabilities.Presence |
            AgentCapabilities.Shortcuts;

        if (target.Workspace is not null)
        {
            capabilities |= AgentCapabilities.Workspace;
        }

        if (target.Sidebar is not null)
        {
            capabilities |= AgentCapabilities.Sidebar;
        }

        if (target.Composer is not null)
        {
            capabilities |= AgentCapabilities.Composer;
        }

        if (target.DeepLinks is not null)
        {
            capabilities |= AgentCapabilities.DeepLinks;
        }

        if (target.Keybindings is not null)
        {
            capabilities |= AgentCapabilities.Keybindings;
        }

        return capabilities;
    }

    private sealed class PresenceAdapter : IAgentPresence
    {
        private readonly CodexCommandService _commands;

        public PresenceAdapter(CodexCommandService commands)
        {
            _commands = commands;
        }

        public bool IsForeground => _commands.IsCodexForeground;

        public bool Wake() => _commands.WakeCodex();
    }

    private sealed class ShortcutAdapter : IAgentShortcuts
    {
        private readonly CodexCommandService _commands;

        public ShortcutAdapter(CodexCommandService commands)
        {
            _commands = commands;
        }

        public bool CanExecute(AppSettings settings)
        {
            return _commands.CanExecute(settings);
        }

        public bool Execute(string shortcut, AppSettings settings)
        {
            return _commands.ExecuteShortcut(shortcut, settings);
        }

        public Task<bool> StepModelAsync(
            int steps,
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            return _commands.StepModelAsync(
                steps,
                settings,
                cancellationToken);
        }
    }

    private sealed class WorkspaceAdapter : IWorkspaceReader
    {
        private readonly CodexDataService _workspace;

        public WorkspaceAdapter(CodexDataService workspace)
        {
            _workspace = workspace;
        }

        public CodexSnapshot LoadSnapshot() => _workspace.LoadSnapshot();

        public bool IsThreadAvailable(string threadId)
        {
            return _workspace.IsThreadAvailable(threadId);
        }

        public IReadOnlyList<SidebarEntry> BuildEntries(
            CodexSnapshot snapshot,
            SidebarScope scope,
            string? selectedProjectPath)
        {
            return _workspace.BuildEntries(
                snapshot,
                scope,
                selectedProjectPath);
        }
    }

    private sealed class SidebarAdapter : ISidebarAutomation
    {
        private readonly CodexSidebarService _sidebar;

        public SidebarAdapter(CodexSidebarService sidebar)
        {
            _sidebar = sidebar;
        }

        public SidebarAutomationResult FocusEntry(
            SidebarEntry entry,
            string? projectName,
            AppSettings settings,
            CancellationToken cancellationToken,
            ProjectDisclosureLease? disclosureLease = null)
        {
            return _sidebar.FocusEntry(
                entry,
                projectName,
                settings,
                cancellationToken,
                disclosureLease);
        }

        public string? TryGetCurrentThreadTitle()
        {
            return _sidebar.TryGetCurrentThreadTitle();
        }

        public SidebarAutomationResult GoBack(AppSettings settings)
        {
            return _sidebar.GoBack(settings);
        }

        public SidebarAutomationResult RestoreDisclosure(
            ProjectDisclosureLease lease)
        {
            return _sidebar.RestoreDisclosure(lease);
        }

        public int? TryGetBottomTaskCount()
        {
            return _sidebar.TryGetBottomTaskCount();
        }
    }

    private sealed class ComposerAdapter : IComposerAutomation
    {
        private readonly CodexComposerService _composer;

        public ComposerAdapter(CodexComposerService composer)
        {
            _composer = composer;
        }

        public ComposerCatalog LoadCatalog() => _composer.LoadCatalog();

        public Task<ComposerAutomationResult> SelectAsync(
            ComposerSettingKind kind,
            string target,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            return _composer.SelectAsync(
                kind,
                target,
                settings,
                cancellationToken);
        }

        public string? TryReadComposerButtonName()
        {
            return _composer.TryReadComposerButtonName();
        }

        public ComposerAutomationResult InvokeAction(
            AppSettings settings,
            params string[] actionNames)
        {
            return _composer.InvokeComposerAction(
                settings,
                actionNames);
        }

        public Task<ComposerAutomationResult> InvokeActionAsync(
            AppSettings settings,
            int timeoutMs,
            CancellationToken cancellationToken,
            params string[] actionNames)
        {
            return _composer.InvokeComposerActionAsync(
                settings,
                timeoutMs,
                cancellationToken,
                actionNames);
        }

        public ComposerAutomationResult Submit(AppSettings settings)
        {
            return _composer.SubmitComposer(settings);
        }

        public ComposerAutomationResult Cancel(AppSettings settings)
        {
            return _composer.CancelComposer(settings);
        }
    }

    private sealed class DeepLinkAdapter : IDeepLinks
    {
        public bool OpenThread(string threadId)
        {
            return CodexCommandService.OpenThread(threadId);
        }

        public void OpenSettings()
        {
            CodexCommandService.OpenCodexSettings();
        }

        public void OpenKeyboardShortcuts()
        {
            CodexCommandService.OpenCodexKeyboardShortcuts();
        }
    }

    private sealed class KeybindingAdapter : IKeybindingProvisioner
    {
        private readonly CodexKeybindingService _keybindings;

        public KeybindingAdapter(CodexKeybindingService keybindings)
        {
            _keybindings = keybindings;
        }

        public string KeybindingsPath => _keybindings.KeybindingsPath;

        public CodexKeybindingMergeResult EnsureBindings(
            AppSettings settings)
        {
            return _keybindings.EnsureBridgeBindings(settings);
        }
    }
}
