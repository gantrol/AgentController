namespace CodexController.Services.Micro;

/// <summary>
/// Allow-listed projection of controller gestures onto physical Codex Micro
/// controls. Semantic ACT-slot actions are gated by the read-only Codex
/// layout; encoder, analog, and Agent-slot methods preserve physical control
/// identity and do not guess a semantic command.
/// </summary>
public sealed class MicroInputService : IDisposable
{
    private const string EncoderPressKey = "ENC";
    private const string EncoderClockwiseKey = "ENC_CW";
    private const string EncoderCounterClockwiseKey = "ENC_CC";

    private readonly IMicroReportTransport _transport;
    private readonly CodexMicroLayoutResolver _layout;
    private readonly object _heldSync = new();
    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private bool _disposed;

    public MicroInputService(IMicroReportTransport transport)
        : this(transport, new CodexMicroLayoutResolver())
    {
    }

    internal MicroInputService(
        IMicroReportTransport transport,
        CodexMicroLayoutResolver layout)
    {
        _transport = transport ??
            throw new ArgumentNullException(nameof(transport));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        if (transport is VhfMicroReportTransport vhf)
        {
            vhf.SlotLightingObserved += Transport_SlotLightingObserved;
        }
    }

    public static MicroInputService Unavailable { get; } = new(
        UnavailableMicroReportTransport.Instance);

    public event EventHandler<MicroSlotLightingSnapshot>?
        SlotLightingObserved;

    public MicroTransportState State => _transport.State;

    public bool HasUnconfirmedPushToTalkState
    {
        get
        {
            lock (_heldSync)
            {
                return _heldKeys.Contains("ACT10");
            }
        }
    }

    public bool TryToggleFast() =>
        WasHandled(SendToggleFast());

    public MicroReportSendResult SendToggleFast() =>
        SendMappedTap(
            "ACT06",
            "ACT06",
            "composer.toggleFastMode");

    public bool TryApprove() => WasHandled(SendApprove());

    public MicroReportSendResult SendApprove() =>
        SendMappedTap("ACT07", "ACT07", "approval.approve");

    public bool TryDecline() => WasHandled(SendDecline());

    public MicroReportSendResult SendDecline() =>
        SendMappedTap("ACT08", "ACT08", "approval.decline");

    public bool TryForkThread() => WasHandled(SendForkThread());

    public MicroReportSendResult SendForkThread() =>
        SendMappedTap("ACT09", "ACT09", "forkThread");

    public bool TrySubmit() => WasHandled(SendSubmit());

    public MicroReportSendResult SendSubmit() =>
        SendMappedTap("ACT12", "ACT12", "composer.submit");

    public bool TrySetPushToTalk(bool pressed) =>
        WasHandled(SendPushToTalk(pressed));

    public MicroReportSendResult SendPushToTalk(bool pressed)
    {
        if (
            pressed &&
            !_layout.AllowsCommand(
                "ACT10_ACT11",
                "dictation.pushToTalk"))
        {
            return MicroReportSendResult.NotSent;
        }

        var releaseHeldKey = false;
        var restartHeldKey = false;
        lock (_heldSync)
        {
            var isHeld = _heldKeys.Contains("ACT10");
            releaseHeldKey = !pressed && isHeld;
            restartHeldKey = pressed && isHeld;
        }

        if (
            !pressed &&
            !releaseHeldKey &&
            !_layout.AllowsCommand(
                "ACT10_ACT11",
                "dictation.pushToTalk"))
        {
            return MicroReportSendResult.NotSent;
        }

        if (restartHeldKey)
        {
            var reports = new List<byte[]>();
            Append(
                reports,
                MicroRpcCodec.EncodeHid("ACT10", action: 0));
            Append(
                reports,
                MicroRpcCodec.EncodeHid("ACT10", action: 1));
            var restart = _transport.Send(reports);
            if (restart is
                MicroReportSendResult.Accepted or
                MicroReportSendResult.OutcomeUnknown)
            {
                lock (_heldSync)
                {
                    _heldKeys.Add("ACT10");
                }
            }

            return restart;
        }

        return SendKeyState("ACT10", pressed);
    }

    public bool TryPressEncoder() =>
        WasHandled(SendEncoderPress());

    public MicroReportSendResult SendEncoderPress() =>
        SendTap(EncoderPressKey);

    public bool TryStepEncoder(int steps) =>
        WasHandled(SendEncoderSteps(steps));

    public MicroReportSendResult SendEncoderSteps(int steps)
    {
        if (steps == 0 || Math.Abs((long)steps) > 64)
        {
            return MicroReportSendResult.NotSent;
        }

        var key = steps > 0
            ? EncoderClockwiseKey
            : EncoderCounterClockwiseKey;
        var reports = new List<byte[]>(Math.Abs(steps));
        for (var index = 0; index < Math.Abs(steps); index++)
        {
            Append(reports, MicroRpcCodec.EncodeHid(key, action: 2));
        }

        return _transport.Send(reports);
    }

    public bool TryTapAgentSlot(int slotIndex) =>
        WasHandled(SendAgentSlot(slotIndex));

    public MicroReportSendResult SendAgentSlot(int slotIndex)
    {
        if (slotIndex is < 0 or > 5)
        {
            return MicroReportSendResult.NotSent;
        }

        return SendTap($"AG{slotIndex:00}");
    }

    public bool TryPulseAnalog(double angle, double distance) =>
        WasHandled(SendAnalogPulse(angle, distance));

    public MicroReportSendResult SendAnalogPulse(
        double angle,
        double distance)
    {
        if (
            !double.IsFinite(angle) ||
            !double.IsFinite(distance) ||
            distance <= 0)
        {
            return MicroReportSendResult.NotSent;
        }

        var reports = new List<byte[]>();
        Append(reports, MicroRpcCodec.EncodeJoystick(0, 0));
        Append(
            reports,
            MicroRpcCodec.EncodeJoystick(
                Math.Clamp(angle, 0, 1),
                Math.Clamp(distance, 0, 1)));
        Append(reports, MicroRpcCodec.EncodeJoystick(angle, 0));
        return _transport.Send(reports);
    }

    /// <summary>
    /// Compatibility shim for the old Simple/Reasoning path. It deliberately
    /// declines when Codex owns the encoder in composer-navigation mode.
    /// </summary>
    public bool TryOpenReasoningControl() =>
        _layout.EncoderMode == CodexMicroLayoutResolver.ReasoningMode &&
        TryPressEncoder();

    /// <summary>
    /// Compatibility shim for the old Simple/Reasoning path. New right-stick
    /// routing should call TryStepEncoder and let Codex interpret the dial.
    /// </summary>
    public bool TryStepReasoning(int steps, bool openFirst)
    {
        if (
            _layout.EncoderMode !=
                CodexMicroLayoutResolver.ReasoningMode ||
            steps == 0)
        {
            return false;
        }

        var reports = new List<byte[]>();
        if (openFirst)
        {
            AppendTap(reports, EncoderPressKey);
        }

        var key = steps > 0
            ? EncoderClockwiseKey
            : EncoderCounterClockwiseKey;
        for (var index = 0; index < Math.Abs(steps); index++)
        {
            Append(reports, MicroRpcCodec.EncodeHid(key, action: 2));
        }

        return WasHandled(_transport.Send(reports));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_transport is VhfMicroReportTransport vhf)
        {
            vhf.SlotLightingObserved -= Transport_SlotLightingObserved;
        }

        BestEffortNeutralize();
        _transport.Dispose();
    }

    internal static bool WasHandled(MicroReportSendResult result) =>
        result != MicroReportSendResult.NotSent;

    private MicroReportSendResult SendMappedTap(
        string layoutSlot,
        string wireKey,
        string commandId) =>
        _layout.AllowsCommand(layoutSlot, commandId)
            ? SendTap(wireKey)
            : MicroReportSendResult.NotSent;

    private MicroReportSendResult SendTap(string key)
    {
        var reports = new List<byte[]>();
        AppendTap(reports, key);
        return _transport.Send(reports);
    }

    private MicroReportSendResult SendKeyState(
        string key,
        bool pressed)
    {
        var result = _transport.Send(
            MicroRpcCodec.EncodeHid(key, pressed ? 1 : 0));
        lock (_heldSync)
        {
            if (
                pressed &&
                result is MicroReportSendResult.Accepted or
                    MicroReportSendResult.OutcomeUnknown)
            {
                _heldKeys.Add(key);
            }
            else if (
                !pressed &&
                result == MicroReportSendResult.Accepted)
            {
                _heldKeys.Remove(key);
            }
        }

        return result;
    }

    private void BestEffortNeutralize()
    {
        try
        {
            var reports = new List<byte[]>();
            string[] heldKeys;
            lock (_heldSync)
            {
                heldKeys = [.. _heldKeys];
                foreach (var key in heldKeys)
                {
                    Append(
                        reports,
                        MicroRpcCodec.EncodeHid(key, action: 0));
                }
            }

            if (reports.Count > 0)
            {
                var result = _transport.Send(reports);
                if (result == MicroReportSendResult.Accepted)
                {
                    lock (_heldSync)
                    {
                        foreach (var key in heldKeys)
                        {
                            _heldKeys.Remove(key);
                        }
                    }
                }
            }
        }
        catch
        {
            // Shutdown neutralization is best effort and is never retried.
        }
    }

    private void Transport_SlotLightingObserved(
        object? sender,
        MicroSlotLightingSnapshot snapshot)
    {
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
                handler(this, snapshot);
            }
            catch
            {
                // Slot lighting is advisory and cannot break input delivery.
            }
        }
    }

    private static void AppendTap(
        List<byte[]> reports,
        string key)
    {
        Append(reports, MicroRpcCodec.EncodeHid(key, action: 1));
        Append(reports, MicroRpcCodec.EncodeHid(key, action: 0));
    }

    private static void Append(
        List<byte[]> destination,
        IReadOnlyList<byte[]> source) =>
        destination.AddRange(source);
}
