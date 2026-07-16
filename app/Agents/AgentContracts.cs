using CodexController.Models;
using CodexController.Services;

namespace CodexController.Agents;

/// <summary>
/// Aggregate port for one controllable AI agent application.
/// Optional capabilities are null when the target cannot provide them.
/// </summary>
public interface IAgentTarget
{
    AgentId Id { get; }
    string DisplayName { get; }
    AgentCapabilities Capabilities { get; }

    IAgentPresence Presence { get; }
    IAgentShortcuts Shortcuts { get; }
    IWorkspaceReader? Workspace { get; }
    ISidebarAutomation? Sidebar { get; }
    IComposerAutomation? Composer { get; }
    IDeepLinks? DeepLinks { get; }
    IKeybindingProvisioner? Keybindings { get; }
}

public interface IAgentPresence
{
    bool IsForeground { get; }
    bool Wake();
}

public interface IAgentShortcuts
{
    bool CanExecute(AppSettings settings);

    bool Execute(string shortcut, AppSettings settings);

    Task<bool> StepModelAsync(
        int steps,
        AppSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Transitional workspace port. The snapshot types remain the current Codex
/// domain objects until the workspace model is generalized in a later slice.
/// </summary>
public interface IWorkspaceReader
{
    CodexSnapshot LoadSnapshot();

    bool IsThreadAvailable(string threadId);

    IReadOnlyList<SidebarEntry> BuildEntries(
        CodexSnapshot snapshot,
        SidebarScope scope,
        string? selectedProjectPath);
}

public interface ISidebarAutomation
{
    SidebarAutomationResult FocusEntry(
        SidebarEntry entry,
        string? projectName,
        AppSettings settings,
        CancellationToken cancellationToken,
        ProjectDisclosureLease? disclosureLease = null);

    string? TryGetCurrentThreadTitle();

    SidebarAutomationResult GoBack(AppSettings settings);

    SidebarAutomationResult RestoreDisclosure(
        ProjectDisclosureLease lease);

    int? TryGetBottomTaskCount();
}

public interface IComposerAutomation
{
    ComposerCatalog LoadCatalog();

    Task<ComposerAutomationResult> SelectAsync(
        ComposerSettingKind kind,
        string target,
        AppSettings settings,
        CancellationToken cancellationToken);

    string? TryReadComposerButtonName();

    ComposerAutomationResult InvokeAction(
        AppSettings settings,
        params string[] actionNames);

    Task<ComposerAutomationResult> InvokeActionAsync(
        AppSettings settings,
        int timeoutMs,
        CancellationToken cancellationToken,
        params string[] actionNames);

    ComposerAutomationResult Submit(AppSettings settings);

    ComposerAutomationResult Cancel(AppSettings settings);
}

public interface IDeepLinks
{
    bool OpenThread(string threadId);
    void OpenSettings();
    void OpenKeyboardShortcuts();
}

public interface IKeybindingProvisioner
{
    string KeybindingsPath { get; }

    CodexKeybindingMergeResult EnsureBindings(AppSettings settings);
}
