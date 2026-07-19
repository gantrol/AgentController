using CodexController.Models;

namespace CodexController.Controllers;

/// <summary>
/// Keeps the stable two-column Micro grid order: Up/Right, Down/Left, then
/// View/Menu. The final row uses the controller family's live glyphs and an
/// explicit View/Menu legend so those center buttons are not mistaken for
/// D-pad directions.
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
