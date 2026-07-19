namespace CodexController.Services.Micro;

public enum MicroTransportState
{
    Unavailable,
    Ready,
    Faulted,
}

public enum MicroReportSendResult
{
    NotSent,
    Accepted,
    OutcomeUnknown,
    Rejected,
}

public interface IMicroReportTransport : IDisposable
{
    MicroTransportState State { get; }

    MicroReportSendResult Send(IReadOnlyList<byte[]> reports);
}

public sealed class UnavailableMicroReportTransport : IMicroReportTransport
{
    public static UnavailableMicroReportTransport Instance { get; } = new();

    private UnavailableMicroReportTransport()
    {
    }

    public MicroTransportState State => MicroTransportState.Unavailable;

    public MicroReportSendResult Send(IReadOnlyList<byte[]> reports) =>
        MicroReportSendResult.NotSent;

    public void Dispose()
    {
    }
}
