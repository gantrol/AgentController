using System.Runtime.InteropServices;
using CodexController.Models;

namespace CodexController.Native;

internal static partial class XInputNative
{
    private const uint ErrorSuccess = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputVibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static partial uint XInputGetState(
        uint userIndex,
        out XInputState state);

    [LibraryImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static partial uint XInputSetState(
        uint userIndex,
        ref XInputVibration vibration);

    public static bool TryGetState(uint userIndex, out ControllerState state)
    {
        var result = XInputGetState(userIndex, out var raw);
        if (result != ErrorSuccess)
        {
            state = ControllerState.Disconnected;
            return false;
        }

        state = new ControllerState(
            true,
            userIndex,
            raw.PacketNumber,
            "XInput",
            (ControllerButtons)raw.Gamepad.Buttons,
            NormalizeThumb(raw.Gamepad.ThumbLX),
            NormalizeThumb(raw.Gamepad.ThumbLY),
            NormalizeThumb(raw.Gamepad.ThumbRX),
            NormalizeThumb(raw.Gamepad.ThumbRY),
            raw.Gamepad.LeftTrigger / 255.0,
            raw.Gamepad.RightTrigger / 255.0);
        return true;
    }

    public static void SetVibration(
        uint userIndex,
        double left,
        double right)
    {
        var vibration = new XInputVibration
        {
            LeftMotorSpeed = (ushort)(Math.Clamp(left, 0, 1) * ushort.MaxValue),
            RightMotorSpeed = (ushort)(Math.Clamp(right, 0, 1) * ushort.MaxValue),
        };
        _ = XInputSetState(userIndex, ref vibration);
    }

    private static double NormalizeThumb(short value)
    {
        return value < 0
            ? value / 32768.0
            : value / 32767.0;
    }
}
