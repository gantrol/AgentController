using CodexController.Models;
using CodexController.Services;

namespace CodexController.Agents;

/// <summary>
/// Null-object capability adapters let shortcut-only agents start without
/// pretending they implement workspace, sidebar, or composer automation.
/// They return stable error codes so presentation can localize the result.
/// </summary>
public static class AgentCapabilityFallbacks
{
    public const string CapabilityUnavailable =
        AgentAutomationErrorCodes.CapabilityUnavailable;

    public static IWorkspaceReader WorkspaceOrEmpty(
        this IAgentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Workspace ?? EmptyWorkspaceReader.Instance;
    }

    public static ISidebarAutomation SidebarOrUnavailable(
        this IAgentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Sidebar ?? UnavailableSidebarAutomation.Instance;
    }

    public static IComposerAutomation ComposerOrUnavailable(
        this IAgentTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Composer ?? UnavailableComposerAutomation.Instance;
    }

    private sealed class EmptyWorkspaceReader : IWorkspaceReader
    {
        public static EmptyWorkspaceReader Instance { get; } = new();

        public CodexSnapshot LoadSnapshot() => new();

        public bool IsThreadAvailable(string threadId) => false;

        public IReadOnlyList<SidebarEntry> BuildEntries(
            CodexSnapshot snapshot,
            SidebarScope scope,
            string? selectedProjectPath) =>
            [];

        public IReadOnlyList<SidebarEntry> BuildUnifiedEntries(
            CodexSnapshot snapshot) =>
            [];
    }

    private sealed class UnavailableSidebarAutomation :
        ISidebarAutomation
    {
        public static UnavailableSidebarAutomation Instance { get; } =
            new();

        public SidebarAutomationResult FocusEntry(
            SidebarEntry entry,
            string? projectName,
            AppSettings settings,
            CancellationToken cancellationToken,
            ProjectDisclosureLease? disclosureLease = null) =>
            Failed();

        public string? TryGetCurrentThreadTitle() => null;

        public SidebarAutomationResult GoBack(AppSettings settings) =>
            Failed();

        public SidebarAutomationResult RestoreDisclosure(
            ProjectDisclosureLease lease) =>
            Failed();

        public int? TryGetBottomTaskCount() => null;

        private static SidebarAutomationResult Failed() =>
            new(false, CapabilityUnavailable);
    }

    private sealed class UnavailableComposerAutomation :
        IComposerAutomation
    {
        public static UnavailableComposerAutomation Instance { get; } =
            new();

        public ComposerCatalog LoadCatalog() =>
            new()
            {
                Models = [],
                InitialModelIndex = 0,
                InitialEffort = string.Empty,
                InitialSpeed = string.Empty,
            };

        public Task<ComposerAutomationResult> SelectAsync(
            ComposerSettingKind kind,
            string target,
            AppSettings settings,
            CancellationToken cancellationToken) =>
            Task.FromResult(Failed());

        public string? TryReadComposerButtonName() => null;

        public string? TryReadDispatchButtonName() => null;

        public bool IsActionAvailable(params string[] actionNames) => false;

        public ComposerAutomationResult InvokeAction(
            AppSettings settings,
            params string[] actionNames) =>
            Failed();

        public Task<ComposerAutomationResult> InvokeActionAsync(
            AppSettings settings,
            int timeoutMs,
            CancellationToken cancellationToken,
            params string[] actionNames) =>
            Task.FromResult(Failed());

        public ComposerDialResult ProbeDialState() =>
            DialFailed();

        public ComposerDialResult DialStep(
            int delta,
            AppSettings settings) =>
            DialFailed();

        public ComposerDialResult DialNavigate(
            ComposerDialNavigation navigation,
            AppSettings settings) =>
            DialFailed();

        public ComposerDialResult DialPress(AppSettings settings) =>
            DialFailed();

        public ComposerDialResult DialSelect(AppSettings settings) =>
            DialFailed();

        public ComposerDialResult DialCancel(AppSettings settings) =>
            DialFailed();

        public ComposerAutomationResult Submit(AppSettings settings) =>
            Failed();

        public ComposerAutomationResult Clear(AppSettings settings) =>
            Failed();

        public ComposerAutomationResult Cancel(AppSettings settings) =>
            Failed();

        private static ComposerAutomationResult Failed() =>
            new(false, CapabilityUnavailable);

        private static ComposerDialResult DialFailed() =>
            new(
                false,
                Error: CapabilityUnavailable);
    }
}
