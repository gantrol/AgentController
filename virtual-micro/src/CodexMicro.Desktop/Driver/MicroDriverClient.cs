using AgentController.MicroBroker;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Driver;

internal sealed class VirtualMicroDriverClient : IDisposable
{
    private readonly MicroBrokerClient _broker = new("CodexMicroSimulator");
    private bool _disposed;

    internal VirtualMicroDriverClient()
    {
        _broker.SlotLightingObserved += (_, snapshot) =>
            SlotLightingObserved?.Invoke(this, snapshot);
    }

    public event EventHandler<SlotLightingSnapshot>?
        SlotLightingObserved;

    public bool SupportsDialogKeyboard { get; private set; }

    public bool IsConnected =>
        !_disposed && _broker.State == MicroBrokerClientState.Ready;

    public DriverInfo Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var info = _broker.Connect();
        SupportsDialogKeyboard =
            (info.Flags & DriverContract.InfoFlagDialogKeyboard) != 0;
        return new(
            info.ConnectionEpoch,
            info.LastBatchSequence,
            info.OutputSequence,
            info.DroppedOutputReports,
            info.Flags,
            info.TransportName);
    }

    public MicroSendResult Submit(IReadOnlyList<byte[]> reports) =>
        _broker.Submit(reports);

    public MicroSendResult TapKeyboardKey(VhfKeyboardKey key, bool shift) =>
        _broker.TapKeyboard(
            key switch
            {
                VhfKeyboardKey.Tab => BrokerKeyboardKey.Tab,
                VhfKeyboardKey.Enter => BrokerKeyboardKey.Enter,
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            },
            shift);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _broker.Dispose();
        SupportsDialogKeyboard = false;
    }
}
