using AgentController.Platform.Controllers;

namespace AgentController.Platform.MacOS.Controllers;

public enum MacControllerLifecycleChangeKind
{
    Connected,
    Disconnected,
    BecameCurrent,
    StoppedBeingCurrent,
}

public sealed record MacControllerLifecycleChange(
    MacControllerLifecycleChangeKind Kind,
    string ControllerId,
    string DisplayName);

internal sealed record MacControllerTopologyUpdate(
    IReadOnlyList<ControllerInputSnapshot> Controllers,
    IReadOnlyList<MacControllerLifecycleChange> Changes,
    string? CurrentControllerId,
    long Revision);

/// <summary>
/// Converts polling snapshots into deterministic topology changes. Input-only
/// changes do not advance the revision; connection and current-controller
/// changes do.
/// </summary>
internal sealed class MacControllerTopologyTracker
{
    private IReadOnlyList<ControllerInputSnapshot> _previous = [];
    private string? _currentControllerId;
    private long _revision;

    internal MacControllerTopologyUpdate Update(
        IReadOnlyList<ControllerInputSnapshot> controllers)
    {
        ArgumentNullException.ThrowIfNull(controllers);
        var currentSnapshot = controllers.ToArray();
        Validate(currentSnapshot);

        var previousById = _previous.ToDictionary(
            controller => controller.Id,
            StringComparer.Ordinal);
        var currentById = currentSnapshot.ToDictionary(
            controller => controller.Id,
            StringComparer.Ordinal);
        var nextCurrentControllerId = currentSnapshot
            .SingleOrDefault(controller => controller.IsCurrent)
            ?.Id;
        var currentChanged = !string.Equals(
            _currentControllerId,
            nextCurrentControllerId,
            StringComparison.Ordinal);
        var changes = new List<MacControllerLifecycleChange>();

        if (currentChanged && _currentControllerId is { } previousCurrentId)
        {
            changes.Add(CreateChange(
                MacControllerLifecycleChangeKind.StoppedBeingCurrent,
                previousCurrentId,
                previousById));
        }

        foreach (var controller in _previous)
        {
            if (!currentById.ContainsKey(controller.Id))
            {
                changes.Add(new(
                    MacControllerLifecycleChangeKind.Disconnected,
                    controller.Id,
                    controller.DisplayName));
            }
        }

        foreach (var controller in currentSnapshot)
        {
            if (!previousById.ContainsKey(controller.Id))
            {
                changes.Add(new(
                    MacControllerLifecycleChangeKind.Connected,
                    controller.Id,
                    controller.DisplayName));
            }
        }

        if (currentChanged && nextCurrentControllerId is { } nextCurrentId)
        {
            changes.Add(CreateChange(
                MacControllerLifecycleChangeKind.BecameCurrent,
                nextCurrentId,
                currentById));
        }

        if (changes.Count > 0)
        {
            _revision++;
        }

        _previous = currentSnapshot;
        _currentControllerId = nextCurrentControllerId;
        return new(
            currentSnapshot,
            changes.ToArray(),
            nextCurrentControllerId,
            _revision);
    }

    private static MacControllerLifecycleChange CreateChange(
        MacControllerLifecycleChangeKind kind,
        string controllerId,
        IReadOnlyDictionary<string, ControllerInputSnapshot> controllers)
    {
        var displayName = controllers.TryGetValue(
            controllerId,
            out var controller)
                ? controller.DisplayName
                : controllerId;
        return new(kind, controllerId, displayName);
    }

    private static void Validate(
        IReadOnlyList<ControllerInputSnapshot> controllers)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var currentCount = 0;
        foreach (var controller in controllers)
        {
            if (string.IsNullOrWhiteSpace(controller.Id))
            {
                throw new InvalidOperationException(
                    "A controller snapshot has no identity.");
            }

            if (!ids.Add(controller.Id))
            {
                throw new InvalidOperationException(
                    $"Controller identity '{controller.Id}' was reported more than once.");
            }

            if (controller.IsCurrent)
            {
                currentCount++;
            }
        }

        if (currentCount > 1)
        {
            throw new InvalidOperationException(
                "Apple Game Controller reported more than one current controller.");
        }
    }
}
