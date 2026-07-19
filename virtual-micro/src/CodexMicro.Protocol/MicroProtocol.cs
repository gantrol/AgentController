namespace CodexMicro.Protocol;

public static class MicroProtocol
{
    public const byte ReportId = 0x06;
    public const byte DebugChannel = 0x01;
    public const byte RpcChannel = 0x02;
    public const int ReportLength = 64;
    public const int MaximumPayloadLength = 61;
    public const int MaximumMessageLength = 64 * 1024;

    public const ushort VendorId = 0x303A;
    public const ushort ProductId = 0x8360;
    public const ushort UsagePage = 0xFF00;

    public const string DeviceType = "project_2077";
    public const string FirmwareVersion = "1.0.0-vhf";
}

public enum MicroSendDisposition
{
    NotSent,
    Accepted,
    OutcomeUnknown,
    Rejected,
}

public readonly record struct MicroSendResult(
    MicroSendDisposition Disposition,
    int AcceptedReports,
    int RequestedReports,
    int NativeStatus,
    string Detail)
{
    public bool WasPossiblySent =>
        Disposition is MicroSendDisposition.Accepted or
            MicroSendDisposition.OutcomeUnknown;

    public static MicroSendResult NotSent(string detail) => new(
        MicroSendDisposition.NotSent,
        0,
        0,
        0,
        detail);
}

public sealed record SlotLighting(
    int SlotId,
    int Color,
    double Brightness,
    int Effect,
    double Speed,
    bool SyncKeysLighting,
    bool SyncAmbientLighting,
    bool LightingAmbiguous);

public sealed record SlotLightingSnapshot(
    long Sequence,
    DateTimeOffset ObservedAt,
    IReadOnlyList<SlotLighting> Slots,
    string MappingKind = "SlotOnly");
