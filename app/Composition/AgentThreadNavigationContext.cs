using AgentController.Application.Navigation;
using CodexController.Agents;
using CodexController.Models;

namespace CodexController.Composition;

internal sealed class AgentThreadNavigationContext :
    IThreadNavigationContext
{
    private readonly AppSettings _settings;
    private readonly Func<IAgentTarget> _activeAgent;

    internal AgentThreadNavigationContext(
        AppSettings settings,
        IWorkspaceReader workspace,
        ISidebarAutomation sidebar)
        : this(
            settings,
            () => new FixedNavigationTarget(workspace, sidebar))
    {
    }

    internal AgentThreadNavigationContext(
        AppSettings settings,
        Func<IAgentTarget> activeAgent)
    {
        _settings = settings ??
            throw new ArgumentNullException(nameof(settings));
        _activeAgent = activeAgent ??
            throw new ArgumentNullException(nameof(activeAgent));
    }

    public bool RequiresForeground =>
        _settings.OnlyWhenCodexForeground;

    public bool IsThreadAvailable(string threadId) =>
        _activeAgent().WorkspaceOrEmpty().IsThreadAvailable(threadId);

    public string? ReadCurrentThreadTitle() =>
        _activeAgent().SidebarOrUnavailable().TryGetCurrentThreadTitle();

    public int CountThreadTitleMatches(string nativeTitle) =>
        _activeAgent().WorkspaceOrEmpty().LoadSnapshot().Threads.Count(thread =>
            string.Equals(
                thread.NativeTitle ?? thread.Title,
                nativeTitle,
                StringComparison.Ordinal));

    private sealed class FixedNavigationTarget : IAgentTarget
    {
        internal FixedNavigationTarget(
            IWorkspaceReader workspace,
            ISidebarAutomation sidebar)
        {
            Workspace = workspace ??
                throw new ArgumentNullException(nameof(workspace));
            Sidebar = sidebar ??
                throw new ArgumentNullException(nameof(sidebar));
        }

        public AgentId Id => new("navigation-target");
        public string DisplayName => "Navigation target";
        public AgentCapabilities Capabilities =>
            AgentCapabilities.Workspace | AgentCapabilities.Sidebar;
        public IAgentPresence Presence => throw new NotSupportedException();
        public IAgentShortcuts Shortcuts => throw new NotSupportedException();
        public IWorkspaceReader Workspace { get; }
        public ISidebarAutomation Sidebar { get; }
        public IComposerAutomation? Composer => null;
        public IDeepLinks? DeepLinks => null;
        public IKeybindingProvisioner? Keybindings => null;
    }
}
