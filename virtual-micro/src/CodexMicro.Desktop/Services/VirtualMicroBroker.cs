using AgentController.MicroBroker;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Logical Micro Surface client. It shares the process-wide broker child with
/// Agent Controller while retaining an independent client id and held-input
/// lease, so either surface can disappear without releasing the other's state.
/// </summary>
internal sealed class VirtualMicroBroker : IDisposable
{
    private const uint InfoFlagDialogKeyboard = 0x00000002;

    private readonly MicroBrokerClient _driver =
        new("AgentController Micro Surface");
    private readonly object _heldSync = new();
    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private bool _supportsDialogKeyboard;
    private bool _disposed;

    public VirtualMicroBroker()
    {
        _driver.SlotLightingObserved += Driver_SlotLightingObserved;
        _driver.StateChanged += Driver_StateChanged;
    }

    public event EventHandler<string>? Log;

    public event EventHandler<string>? StateChanged;

    public event EventHandler<SlotLightingSnapshot>? SlotLightingObserved;

    public bool IsReady =>
        !_disposed && _driver.State == MicroBrokerClientState.Ready;

    public bool SupportsDialogKeyboard =>
        IsReady && _supportsDialogKeyboard;

    public BrokerDriverInfo Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var info = _driver.Connect();
        _supportsDialogKeyboard =
            (info.Flags & InfoFlagDialogKeyboard) != 0;
        PublishState(_driver.State);
        Log?.Invoke(
            this,
            info.CodexLinkObserved
                ? $"{info.TransportName} ready · epoch {info.ConnectionEpoch:X16} · drops {info.DroppedOutputReports}"
                : $"{info.TransportName} connected · waiting for Codex runtime handshake");
        return info;
    }

    public Task<MicroSendResult> TapKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key == "ACT10_ACT11")
        {
            key = "ACT10";
        }

        var reports = new List<byte[]>();
        reports.AddRange(MicroRpcCodec.EncodeHid(key, 1));
        reports.AddRange(MicroRpcCodec.EncodeHid(key, 0));
        return SubmitAsync(reports, $"tap {key}");
    }

    public async Task<MicroSendResult> SetKeyAsync(
        string key,
        bool pressed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var result = await SubmitAsync(
            MicroRpcCodec.EncodeHid(key, pressed ? 1 : 0),
            $"{key} {(pressed ? "down" : "up")}").ConfigureAwait(false);

        lock (_heldSync)
        {
            if (pressed && result.Disposition is not (
                MicroSendDisposition.NotSent or
                MicroSendDisposition.Rejected))
            {
                _heldKeys.Add(key);
            }
            else if (!pressed)
            {
                _heldKeys.Remove(key);
            }
        }

        return result;
    }

    public Task<MicroSendResult> StepEncoderAsync(bool clockwise) =>
        SubmitAsync(
            MicroRpcCodec.EncodeHid(
                clockwise ? "ENC_CW" : "ENC_CC",
                2),
            clockwise
                ? "encoder clockwise"
                : "encoder counter-clockwise");

    public Task<MicroSendResult> TapDialogKeyAsync(
        BrokerKeyboardKey key,
        bool shift = false)
    {
        if (!SupportsDialogKeyboard)
        {
            return Task.FromResult(MicroSendResult.NotSent(
                "The installed VHF driver does not expose the restricted " +
                "dialog keyboard collection."));
        }

        return Task.Run(() =>
        {
            var result = _driver.TapKeyboard(key, shift);
            Log?.Invoke(
                this,
                $"VHF dialog key · {(shift ? "Shift+" : string.Empty)}{key} · {result.Disposition}");
            return result;
        });
    }

    public async Task<MicroSendResult> OpenCodexMicroSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        var press = await SubmitAsync(
            MicroRpcCodec.EncodeHid("ENC", 1),
            "encoder hold press").ConfigureAwait(false);
        if (press.Disposition is MicroSendDisposition.NotSent or
            MicroSendDisposition.Rejected)
        {
            return press;
        }

        lock (_heldSync)
        {
            _heldKeys.Add("ENC");
        }

        MicroSendResult release = default;
        try
        {
            await Task.Delay(650, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            release = await SubmitAsync(
                MicroRpcCodec.EncodeHid("ENC", 0),
                "encoder hold release").ConfigureAwait(false);
            lock (_heldSync)
            {
                _heldKeys.Remove("ENC");
            }
        }

        if (
            press.Disposition != MicroSendDisposition.Accepted ||
            release.Disposition != MicroSendDisposition.Accepted)
        {
            return new MicroSendResult(
                MicroSendDisposition.OutcomeUnknown,
                press.AcceptedReports + release.AcceptedReports,
                press.RequestedReports + release.RequestedReports,
                release.NativeStatus != 0
                    ? release.NativeStatus
                    : press.NativeStatus,
                "The encoder hold may have reached Codex; it is not retried.");
        }

        return new MicroSendResult(
            MicroSendDisposition.Accepted,
            2,
            2,
            0,
            "The encoder hold was delivered; Codex owns the settings navigation result.");
    }

    public Task<MicroSendResult> SetJoystickStateAsync(
        double angle,
        double distance,
        string direction) =>
        SubmitAsync(
            MicroRpcCodec.EncodeJoystick(angle, distance),
            $"analog {direction} {distance:F2}");

    public Task<MicroSendResult> MoveJoystickAsync(
        double angle,
        double distance,
        string direction)
    {
        var reports = new List<byte[]>();
        reports.AddRange(MicroRpcCodec.EncodeJoystick(0, 0));
        reports.AddRange(MicroRpcCodec.EncodeJoystick(angle, distance));
        reports.AddRange(MicroRpcCodec.EncodeJoystick(angle, 0));
        return SubmitAsync(reports, $"analog {direction}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BestEffortNeutralize();
        _disposed = true;
        _driver.SlotLightingObserved -= Driver_SlotLightingObserved;
        _driver.StateChanged -= Driver_StateChanged;
        _driver.Dispose();
        _supportsDialogKeyboard = false;
    }

    private Task<MicroSendResult> SubmitAsync(
        IReadOnlyList<byte[]> reports,
        string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady)
        {
            return Task.FromResult(MicroSendResult.NotSent(
                "Codex has not completed the runtime Micro handshake."));
        }

        return Task.Run(() =>
        {
            var result = _driver.Submit(reports);
            Log?.Invoke(
                this,
                $"Micro → Codex · {label} · {result.Disposition}");
            return result;
        });
    }

    private void BestEffortNeutralize()
    {
        if (!IsReady)
        {
            return;
        }

        try
        {
            var reports = new List<byte[]>();
            lock (_heldSync)
            {
                foreach (var key in _heldKeys)
                {
                    reports.AddRange(MicroRpcCodec.EncodeHid(key, 0));
                }

                _heldKeys.Clear();
            }

            reports.AddRange(MicroRpcCodec.EncodeJoystick(0, 0));
            _ = _driver.Submit(reports);
        }
        catch
        {
        }
    }

    private void Driver_SlotLightingObserved(
        object? sender,
        SlotLightingSnapshot snapshot) =>
        SlotLightingObserved?.Invoke(this, snapshot);

    private void Driver_StateChanged(
        object? sender,
        MicroBrokerClientState state) =>
        PublishState(state);

    private void PublishState(MicroBrokerClientState state) =>
        StateChanged?.Invoke(
            this,
            state switch
            {
                MicroBrokerClientState.Ready => "ready",
                MicroBrokerClientState.Faulted =>
                    "faulted:Micro Broker is unavailable.",
                _ => "waiting-runtime-handshake",
            });
}
