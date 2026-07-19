using CodexMicro.Desktop.Driver;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

internal sealed class VirtualMicroBroker : IDisposable
{
    private readonly VirtualMicroDriverClient _driver = new();
    private readonly object _heldSync = new();
    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private bool _compatible;
    private bool _disposed;

    public VirtualMicroBroker()
    {
        _driver.SlotLightingObserved += (_, snapshot) =>
            SlotLightingObserved?.Invoke(this, snapshot);
    }

    public event EventHandler<string>? Log;

    public event EventHandler<string>? StateChanged;

    public event EventHandler<SlotLightingSnapshot>? SlotLightingObserved;

    public bool IsReady => _compatible && _driver.IsConnected;

    public bool SupportsDialogKeyboard =>
        IsReady && _driver.SupportsDialogKeyboard;

    public DriverInfo Connect(CodexCompatibilityResult compatibility)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(compatibility);
        if (!compatibility.IsCompatible)
        {
            throw new InvalidOperationException(compatibility.Detail);
        }

        var info = _driver.Connect();
        _compatible = true;
        StateChanged?.Invoke(this, "ready");
        Log?.Invoke(
            this,
            $"{info.TransportName} ready · epoch {info.ConnectionEpoch:X16} · drops {info.DroppedOutputReports}");
        return info;
    }

    public Task<MicroSendResult> TapKeyAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key == "ACT10_ACT11")
        {
            // Current renderer treats the double keycap as ACT10 and ignores
            // ACT11 to prevent a duplicate command.
            key = "ACT10";
        }

        var reports = new List<byte[]>();
        reports.AddRange(MicroRpcCodec.EncodeHid(key, 1));
        reports.AddRange(MicroRpcCodec.EncodeHid(key, 0));
        return SubmitAsync(reports, $"tap {key}");
    }

    public Task<MicroSendResult> WakeLightingAsync() =>
        SubmitAsync(
            CodexLightingWakeInstruction.Encode(),
            "lighting wake (ignored ACT11 release)");

    public async Task<MicroSendResult> SetKeyAsync(
        string key,
        bool pressed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var result = await SubmitAsync(
            MicroRpcCodec.EncodeHid(key, pressed ? 1 : 0),
            $"{key} {(pressed ? "down" : "up")}").ConfigureAwait(false);

        // OutcomeUnknown is tracked as held on key-down so shutdown still
        // attempts a neutral report. Release always clears local hold state.
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
            clockwise ? "encoder clockwise" : "encoder counter-clockwise");

    public Task<MicroSendResult> TapDialogKeyAsync(
        VhfKeyboardKey key,
        bool shift = false)
    {
        EnsureReady();
        if (!_driver.SupportsDialogKeyboard)
        {
            throw new InvalidOperationException(
                "The installed VHF driver does not expose the restricted dialog keyboard collection.");
        }

        return Task.Run(() =>
        {
            var result = _driver.TapKeyboardKey(key, shift);
            Log?.Invoke(
                this,
                $"VHF dialog key · {(shift ? "Shift+" : string.Empty)}{key} · {result.Disposition}");
            return result;
        });
    }

    public async Task<MicroSendResult> OpenCodexMicroSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureReady();
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
            // Codex 26.715.3651.0 uses a 500 ms hold timer before navigating
            // to /settings/codex-micro. Keep a small scheduling margin.
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
                "The 650 ms encoder hold may have reached Codex; it is not retried.");
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
        string direction)
        => SubmitAsync(
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

        _disposed = true;
        BestEffortNeutralize();
        _driver.Dispose();
        _compatible = false;
    }

    private Task<MicroSendResult> SubmitAsync(
        IReadOnlyList<byte[]> reports,
        string label)
    {
        EnsureReady();
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
        if (!_driver.IsConnected)
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
            // Shutdown neutralization is best effort and is never retried.
        }
    }

    private void EnsureReady()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsReady)
        {
            throw new InvalidOperationException(
                "Codex compatibility and the virtual HID connection must both be ready.");
        }
    }
}

internal static class CodexLightingWakeInstruction
{
    // Codex's Micro service treats every HID report as lighting activity, but
    // its bridge explicitly ignores ACT11 because the physical double keycap
    // is represented by ACT10. A release cannot leave held input behind.
    public const string Key = "ACT11";
    public const int Action = 0;

    public static IReadOnlyList<byte[]> Encode() =>
        MicroRpcCodec.EncodeHid(Key, Action);
}
