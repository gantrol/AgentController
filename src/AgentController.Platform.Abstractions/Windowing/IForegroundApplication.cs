namespace AgentController.Platform.Windowing;

/// <summary>
/// Cross-platform capability for observing and activating the target desktop
/// application without exposing Win32, AppKit, process, or window handles.
/// </summary>
public interface IForegroundApplication
{
    bool IsForeground { get; }

    bool TryActivate();
}
