namespace CodexController.Services.Micro;

/// <summary>
/// Stable, allow-listed projection of controller gestures onto the observed
/// Codex Micro HID notification ABI. Arbitrary methods and raw reports are not
/// exposed to presentation code.
/// </summary>
public sealed class MicroInputService : IDisposable
{
    private const string EncoderPressKey = "ENC";
    private const string EncoderClockwiseKey = "ENC_CW";
    private const string EncoderCounterClockwiseKey = "ENC_CC";
    private const string FastCommandKey = "ACT06";
    private const string ForkCommandKey = "ACT09";
    private const string EscapeAgentKey = "AG00";

    private readonly IMicroReportTransport _transport;

    public MicroInputService(IMicroReportTransport transport)
    {
        _transport = transport ??
            throw new ArgumentNullException(nameof(transport));
    }

    public static MicroInputService Unavailable { get; } = new(
        UnavailableMicroReportTransport.Instance);

    public MicroTransportState State => _transport.State;

    public bool TryToggleFast() => TryTap(FastCommandKey);

    public bool TryForkThread() => TryTap(ForkCommandKey);

    public bool TryOpenReasoningControl() => TryTap(EncoderPressKey);

    public bool TryDismissOpenMenu() => TryTap(EscapeAgentKey);

    public bool TryStepReasoning(int steps, bool openFirst)
    {
        if (steps == 0)
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

        return _transport.TrySend(reports);
    }

    public void Dispose() => _transport.Dispose();

    private bool TryTap(string key)
    {
        var reports = new List<byte[]>();
        AppendTap(reports, key);
        return _transport.TrySend(reports);
    }

    private static void AppendTap(List<byte[]> reports, string key)
    {
        Append(reports, MicroRpcCodec.EncodeHid(key, action: 1));
        Append(reports, MicroRpcCodec.EncodeHid(key, action: 0));
    }

    private static void Append(
        List<byte[]> destination,
        IReadOnlyList<byte[]> source)
    {
        destination.AddRange(source);
    }
}
