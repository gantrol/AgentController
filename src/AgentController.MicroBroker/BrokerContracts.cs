using CodexMicro.Protocol;

namespace AgentController.MicroBroker;

public static class MicroBrokerProtocol
{
    public const int Version = 1;
    public const string PipeName = "AgentController.CodexMicroBroker.v1";
    public const int MaximumFrameLength = 128 * 1024;
    public const int MaximumBatchReports = 64;
    public const int ClientLeaseTimeoutMs = 3_500;

    public const string Hello = "hello";
    public const string Submit = "submit";
    public const string Keyboard = "keyboard";
    public const string Poll = "poll";
    public const string Disconnect = "disconnect";
}

public enum BrokerKeyboardKey
{
    Tab,
    Enter,
}

public enum BrokerConnectionRole
{
    Client,
    DriverOwner,
}

public sealed record BrokerDriverInfo(
    ulong ConnectionEpoch,
    ulong LastBatchSequence,
    ulong OutputSequence,
    uint DroppedOutputReports,
    uint Flags,
    string TransportName,
    bool CodexLinkObserved = false);

public sealed record BrokerEvent(
    long Sequence,
    string Kind,
    SlotLightingSnapshot? SlotLighting = null,
    string? Detail = null);

public sealed record BrokerRequest(
    int Version,
    string Operation,
    Guid ClientId,
    long RequestId,
    string ClientName,
    byte[][]? Reports = null,
    BrokerKeyboardKey? KeyboardKey = null,
    bool Shift = false,
    long EventCursor = 0);

public sealed record BrokerResponse(
    int Version,
    long RequestId,
    bool Succeeded,
    string? Error = null,
    BrokerDriverInfo? Driver = null,
    MicroSendResult? Send = null,
    BrokerEvent[]? Events = null,
    long EventCursor = 0,
    BrokerConnectionRole Role = BrokerConnectionRole.Client);
