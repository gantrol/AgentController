using CodexController.Controllers;

namespace CodexController.Presentation;

public enum ControllerControlAnchor
{
    DPadUp,
    DPadRight,
    DPadDown,
    DPadLeft,
    View,
    Menu,
    FaceNorth,
    FaceEast,
    FaceSouth,
    FaceWest,
    LeftStick,
    RightStick,
    Guide,
}

/// <summary>
/// Resolves a logical shortcut to the named visual anchor on the controller.
/// The view draws to these anchors instead of inferring a target from a slot's
/// screen position.
/// </summary>
public static class RadialMenuControlAnchorMap
{
    public static bool TryResolve(
        LogicalInput input,
        out ControllerControlAnchor anchor)
    {
        anchor = input switch
        {
            LogicalInput.DPadUp => ControllerControlAnchor.DPadUp,
            LogicalInput.DPadRight => ControllerControlAnchor.DPadRight,
            LogicalInput.DPadDown => ControllerControlAnchor.DPadDown,
            LogicalInput.DPadLeft => ControllerControlAnchor.DPadLeft,
            LogicalInput.View => ControllerControlAnchor.View,
            LogicalInput.Menu => ControllerControlAnchor.Menu,
            LogicalInput.FaceNorth => ControllerControlAnchor.FaceNorth,
            LogicalInput.FaceEast => ControllerControlAnchor.FaceEast,
            LogicalInput.FaceSouth => ControllerControlAnchor.FaceSouth,
            LogicalInput.FaceWest => ControllerControlAnchor.FaceWest,
            LogicalInput.LeftStick or LogicalInput.LeftStickPress =>
                ControllerControlAnchor.LeftStick,
            LogicalInput.RightStick or LogicalInput.RightStickPress =>
                ControllerControlAnchor.RightStick,
            LogicalInput.Guide => ControllerControlAnchor.Guide,
            _ => default,
        };

        return input is
            LogicalInput.DPadUp or
            LogicalInput.DPadRight or
            LogicalInput.DPadDown or
            LogicalInput.DPadLeft or
            LogicalInput.View or
            LogicalInput.Menu or
            LogicalInput.FaceNorth or
            LogicalInput.FaceEast or
            LogicalInput.FaceSouth or
            LogicalInput.FaceWest or
            LogicalInput.LeftStick or
            LogicalInput.LeftStickPress or
            LogicalInput.RightStick or
            LogicalInput.RightStickPress or
            LogicalInput.Guide;
    }
}
