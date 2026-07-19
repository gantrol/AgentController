namespace CodexMicro.Desktop.Driver;

internal static class DriverContract
{
    public const uint InfoFlagDialogKeyboard = 0x00000002;
}

internal enum VhfKeyboardKey : byte
{
    Enter = 0x28,
    Tab = 0x2B,
}

internal sealed record DriverInfo(
    ulong ConnectionEpoch,
    ulong LastBatchSequence,
    ulong OutputSequence,
    uint DroppedOutputReports,
    uint Flags,
    string TransportName);
