using AgentController.Application.Navigation;
using CodexController.Agents;
using CodexController.Models;

namespace CodexController.Composition;

internal sealed class AgentThreadNavigationContext :
    IThreadNavigationContext
{
    private readonly AppSettings _settings;
    private readonly IWorkspaceReader _workspace;
    private readonly ISidebarAutomation _sidebar;

    internal AgentThreadNavigationContext(
        AppSettings settings,
        IWorkspaceReader workspace,
        ISidebarAutomation sidebar)
    {
        _settings = settings ??
            throw new ArgumentNullException(nameof(settings));
        _workspace = workspace ??
            throw new ArgumentNullException(nameof(workspace));
        _sidebar = sidebar ??
            throw new ArgumentNullException(nameof(sidebar));
    }

    public bool RequiresForeground =>
        _settings.OnlyWhenCodexForeground;

    public bool IsThreadAvailable(string threadId) =>
        _workspace.IsThreadAvailable(threadId);

    public string? ReadCurrentThreadTitle() =>
        _sidebar.TryGetCurrentThreadTitle();

    public int CountThreadTitleMatches(string nativeTitle) =>
        _workspace.LoadSnapshot().Threads.Count(thread =>
            string.Equals(
                thread.NativeTitle ?? thread.Title,
                nativeTitle,
                StringComparison.Ordinal));
}
