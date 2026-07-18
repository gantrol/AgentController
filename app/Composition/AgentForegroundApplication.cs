using AgentController.Platform.Windowing;
using CodexController.Agents;

namespace CodexController.Composition;

internal sealed class AgentForegroundApplication :
    IForegroundApplication
{
    private readonly IAgentPresence _presence;

    internal AgentForegroundApplication(IAgentPresence presence)
    {
        _presence = presence ??
            throw new ArgumentNullException(nameof(presence));
    }

    public bool IsForeground => _presence.IsForeground;

    public bool TryActivate() => _presence.Wake();
}
