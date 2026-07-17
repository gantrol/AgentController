using CodexController.Models;

namespace CodexController.Controllers;

/// <summary>
/// Keeps the stable Agent slot order while placing each shortcut beside the
/// controller control it represents. View/Menu occupy the upper side sectors;
/// D-pad left/right occupy the lower side sectors.
/// </summary>
public static class AgentRadialSlotLayout
{
    public static IReadOnlyList<AgentRadialSlotBinding> Bindings { get; } =
    [
        new(LogicalInput.DPadUp, RadialMenuSlotPosition.Top),
        new(LogicalInput.DPadRight, RadialMenuSlotPosition.CenterRight),
        new(LogicalInput.DPadDown, RadialMenuSlotPosition.Bottom),
        new(LogicalInput.DPadLeft, RadialMenuSlotPosition.CenterLeft),
        new(LogicalInput.View, RadialMenuSlotPosition.Left),
        new(LogicalInput.Menu, RadialMenuSlotPosition.Right),
    ];
}

public readonly record struct AgentRadialSlotBinding(
    LogicalInput Input,
    RadialMenuSlotPosition Position);
