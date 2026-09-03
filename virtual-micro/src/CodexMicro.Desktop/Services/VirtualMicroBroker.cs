using System.Diagnostics.CodeAnalysis;
using AgentController.MicroBroker;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Logical Micro keypad client. It shares the machine-local broker while
/// retaining an independent client id and held-input lease, so either product
/// can disappear without releasing the other's state.
/// </summary>
internal sealed class VirtualMicroBroker : IDisposable
{
    private readonly MicroBrokerClient _driver =
        new("Codex Micro Keypad");
    private readonly object _heldSync = new();
    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private bool _disposed;

    public VirtualMicroBroker()
    {
        _driver.SlotLightingObserved += Driver_SlotLightingObserved;
        _driver.StateChanged += Driver_StateChanged;
        _driver.CodexLinkObservedChanged +=
            Driver_CodexLinkObservedChanged;
    }

    public event EventHandler<string>? Log;

    public event EventHandler<string>? StateChanged;

    public event EventHandler<SlotLightingSnapshot>? SlotLightingObserved;

    public bool IsReady =>
        !_disposed && _driver.State == MicroBrokerClientState.Ready;

    public bool CodexLinkObserved =>
        !_disposed && _driver.CodexLinkObserved;

    public void StartConnecting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _driver.StartConnecting();
    }

    public BrokerDriverInfo Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var info = _driver.Connect();
        PublishConnected(info);
        return info;
    }

    public bool TryConnect(
        [NotNullWhen(true)] out BrokerDriverInfo? info,
        out string error)
    {
        if (_disposed)
        {
            info = null;
            error = "The Codex Micro keypad transport is closed.";
            return false;
        }

        if (!_driver.TryConnect(out info, out error))
        {
            PublishState(_driver.State);
            return false;
        }

        PublishConnected(info);
        return true;
    }

    private void PublishConnected(BrokerDriverInfo info)
    {
        PublishState(_driver.State);
        Log?.Invoke(
            this,
            info.CodexLinkObserved
                ? $"{info.TransportName} ready · epoch {info.ConnectionEpoch:X16} · drops {info.DroppedOutputReports}"
                : $"{info.TransportName} connected · direct HID ready; Codex output not yet observed");
    }

    public Task<BrokerDriverInfo> RecoverCodexLinkAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.Run(() =>
        {
            Log?.Invoke(this, "Rebuilding the Codex Micro HID transport...");
            var info = _driver.RecoverCodexLink();
            PublishState(_driver.State);
            Log?.Invoke(
                this,
                $"Codex Micro HID rebuilt; waiting for Codex handshake " +
                $"(epoch {info.ConnectionEpoch:X16}).");
            return info;
        });
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
        _driver.CodexLinkObservedChanged -=
            Driver_CodexLinkObservedChanged;
        _driver.Dispose();
    }

    private Task<MicroSendResult> SubmitAsync(
        IReadOnlyList<byte[]> reports,
        string label)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady)
        {
            return Task.FromResult(MicroSendResult.NotSent(
                "The shared Micro Broker is not connected to the virtual HID."));
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

    private void Driver_CodexLinkObservedChanged(
        object? sender,
        bool observed) =>
        PublishState(_driver.State);

    private void PublishState(MicroBrokerClientState state) =>
        StateChanged?.Invoke(
            this,
            state switch
            {
                MicroBrokerClientState.Ready =>
                    CodexLinkObserved ? "ready" : "transport-ready",
                MicroBrokerClientState.Faulted =>
                    "faulted:Micro Broker is unavailable.",
                _ => "unavailable",
            });
}
