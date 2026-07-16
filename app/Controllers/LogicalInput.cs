namespace CodexController.Controllers;

/// <summary>
/// Controller inputs expressed by physical position instead of a vendor's
/// printed label. This keeps bindings stable across ABXY layout variants.
/// </summary>
public enum LogicalInput
{
    FaceSouth,
    FaceEast,
    FaceWest,
    FaceNorth,
    LeftStick,
    RightStick,
    LeftStickPress,
    RightStickPress,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    View,
    Menu,
    Guide,
    LeftAuxiliary,
    RightAuxiliary,
}
