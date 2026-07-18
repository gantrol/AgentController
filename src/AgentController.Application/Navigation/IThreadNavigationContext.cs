namespace AgentController.Application.Navigation;

/// <summary>
/// Application-owned read port for the mutable state needed by thread open
/// confirmation and navigation undo. Adapter-specific workspace and UI types
/// must not cross this boundary.
/// </summary>
public interface IThreadNavigationContext
{
    bool RequiresForeground { get; }

    bool IsThreadAvailable(string threadId);

    string? ReadCurrentThreadTitle();

    int CountThreadTitleMatches(string nativeTitle);
}
