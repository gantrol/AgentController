using AgentController.MicroBroker;
using CodexMicro.Protocol;

namespace CodexController.Services.Micro;

/// <summary>
/// Current-user client for the single Micro Broker. The desktop UI never
/// opens CodexMicroVhfUm directly; the hidden broker process owns sequence,
/// output/RPC, and per-client neutralization.
/// </summary>
public sealed class VhfMicroReportTransport : IMicroReportTransport
{
    private readonly MicroBrokerClient _broker = new("AgentController");
    private bool _disposed;

    public VhfMicroReportTransport()
    {
        _broker.SlotLightingObserved += Broker_SlotLightingObserved;
        _broker.StartConnecting();
    }

    public event EventHandler<MicroSlotLightingSnapshot>?
        SlotLightingObserved;

    public MicroTransportState State => _broker.State switch
    {
        MicroBrokerClientState.Ready => MicroTransportState.Ready,
        MicroBrokerClientState.Faulted => MicroTransportState.Faulted,
        _ => MicroTransportState.Unavailable,
    };

    public MicroReportSendResult Send(IReadOnlyList<byte[]> reports)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = _broker.Submit(reports);
        return result.Disposition switch
        {
            MicroSendDisposition.Accepted =>
                MicroReportSendResult.Accepted,
            MicroSendDisposition.OutcomeUnknown =>
                MicroReportSendResult.OutcomeUnknown,
            MicroSendDisposition.Rejected =>
                MicroReportSendResult.Rejected,
            _ => MicroReportSendResult.NotSent,
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _broker.SlotLightingObserved -= Broker_SlotLightingObserved;
        _broker.Dispose();
    }

    private void Broker_SlotLightingObserved(
        object? sender,
        SlotLightingSnapshot snapshot)
    {
        var mapped = new MicroSlotLightingSnapshot(
            snapshot.Sequence,
            snapshot.ObservedAt,
            snapshot.Slots
                .Select(slot => new MicroSlotLighting(
                    slot.SlotId,
                    slot.Color,
                    slot.Brightness,
                    slot.Effect,
                    slot.Speed,
                    slot.SyncKeysLighting,
                    slot.SyncAmbientLighting,
                    slot.LightingAmbiguous))
                .ToArray());
        var handlers = SlotLightingObserved;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<MicroSlotLightingSnapshot> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(this, mapped);
            }
            catch
            {
                // Broker state is observational; UI subscribers cannot stop
                // the shared device channel.
            }
        }
    }
}
