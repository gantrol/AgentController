namespace CodexController.Models;

[Flags]
public enum ControllerButtons : ushort
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftThumb = 0x0040,
    RightThumb = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
}

public readonly record struct ControllerState(
    bool IsConnected,
    uint UserIndex,
    uint PacketNumber,
    string Backend,
    ControllerButtons Buttons,
    double LeftX,
    double LeftY,
    double RightX,
    double RightY,
    double LeftTrigger,
    double RightTrigger)
{
    public static ControllerState Disconnected => new(
        false,
        0,
        0,
        "None",
        ControllerButtons.None,
        0,
        0,
        0,
        0,
        0,
        0);
}
