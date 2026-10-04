using CodexController.Core.Bridge;
using CodexController.Models;

namespace CodexController.Controllers;

public readonly record struct TutorialInput(
    ControllerTutorialMode Mode,
    LogicalInput Input,
    int Direction = 0)
{
    public RadialMenuLayerKind? Layer => Mode switch
    {
        ControllerTutorialMode.Action => RadialMenuLayerKind.Action,
        ControllerTutorialMode.Agent => RadialMenuLayerKind.Agent,
        ControllerTutorialMode.Turn => RadialMenuLayerKind.Turn,
        ControllerTutorialMode.Command => RadialMenuLayerKind.Command,
        _ => null,
    };

    public bool IsLayerSelector => Input is LogicalInput.LeftShoulder
        or LogicalInput.RightShoulder or LogicalInput.RightTrigger;

    public RadialInputAction Action => Layer is { } layer
        ? RadialInputMap.Resolve(layer, Button)
        : RadialInputAction.None;

    public bool IsVoice => Input == LogicalInput.LeftTrigger ||
        Action == RadialInputAction.PushToTalk;

    public int HoldMilliseconds => IsVoice || IsLayerSelector ? 0 :
        Action == RadialInputAction.BeginStopHold ? BridgeTimings.CancelHoldMs :
        Layer is not null ? 0 : Input switch
        {
            LogicalInput.FaceEast => BridgeTimings.CancelHoldMs,
            LogicalInput.RightStickPress => BridgeTimings.DialHoldMs,
            LogicalInput.DPadUp => BridgeTimings.ConversationTopHoldMs,
            LogicalInput.DPadDown => BridgeTimings.ConversationBottomHoldMs,
            _ => 0,
        };

    public ControllerButtons Button => Input switch
    {
        LogicalInput.FaceSouth => ControllerButtons.A,
        LogicalInput.FaceEast => ControllerButtons.B,
        LogicalInput.FaceWest => ControllerButtons.X,
        LogicalInput.FaceNorth => ControllerButtons.Y,
        LogicalInput.DPadUp => ControllerButtons.DPadUp,
        LogicalInput.DPadRight => ControllerButtons.DPadRight,
        LogicalInput.DPadDown => ControllerButtons.DPadDown,
        LogicalInput.DPadLeft => ControllerButtons.DPadLeft,
        LogicalInput.View => ControllerButtons.Back,
        LogicalInput.Menu => ControllerButtons.Start,
        LogicalInput.LeftStickPress => ControllerButtons.LeftThumb,
        LogicalInput.RightStickPress => ControllerButtons.RightThumb,
        _ => ControllerButtons.None,
    };

    public string ArtworkLayer => Input switch
    {
        LogicalInput.FaceSouth => "a", LogicalInput.FaceEast => "b",
        LogicalInput.FaceWest => "x", LogicalInput.FaceNorth => "y",
        LogicalInput.LeftShoulder => "lb", LogicalInput.RightShoulder => "rb",
        LogicalInput.LeftTrigger => "lt", LogicalInput.RightTrigger => "rt",
        LogicalInput.View => "view", LogicalInput.Menu => "menu",
        LogicalInput.DPadUp => "dpad-up", LogicalInput.DPadDown => "dpad-down",
        LogicalInput.DPadLeft => "dpad-left", LogicalInput.DPadRight => "dpad-right",
        LogicalInput.LeftStick or LogicalInput.LeftStickPress => "left-stick",
        LogicalInput.RightStick or LogicalInput.RightStickPress => "right-stick",
        _ => string.Empty,
    };
}
