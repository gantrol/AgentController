using System.Globalization;

namespace AgentController.Platform.MacOS.Controllers;

/// <summary>
/// Assigns a process-session identity to each native GCController instance.
/// Native object handles are stable while a controller remains connected, but
/// array positions are not stable when multiple controllers connect or leave.
/// </summary>
internal sealed class MacControllerIdentityMap
{
    private readonly Dictionary<nint, string> _identities = [];
    private long _nextIdentity;

    internal string GetOrAdd(nint controller)
    {
        if (controller == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(controller),
                "A native controller handle is required.");
        }

        if (_identities.TryGetValue(controller, out var identity))
        {
            return identity;
        }

        _nextIdentity++;
        identity = "apple-gamecontroller:session-" +
            _nextIdentity.ToString("D4", CultureInfo.InvariantCulture);
        _identities.Add(controller, identity);
        return identity;
    }

    internal void RetainOnly(IEnumerable<nint> connectedControllers)
    {
        ArgumentNullException.ThrowIfNull(connectedControllers);
        var connected = connectedControllers.ToHashSet();
        foreach (var controller in _identities.Keys.ToArray())
        {
            if (!connected.Contains(controller))
            {
                _identities.Remove(controller);
            }
        }
    }
}
