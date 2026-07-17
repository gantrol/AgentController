using System.IO;
using System.IO.Pipes;

namespace CodexController.Services.Micro;

public enum MicroTransportState
{
    Unavailable,
    Ready,
    Faulted,
}

public interface IMicroReportTransport : IDisposable
{
    MicroTransportState State { get; }

    bool TrySend(IReadOnlyList<byte[]> reports);
}

public sealed class UnavailableMicroReportTransport : IMicroReportTransport
{
    public static UnavailableMicroReportTransport Instance { get; } = new();

    private UnavailableMicroReportTransport()
    {
    }

    public MicroTransportState State => MicroTransportState.Unavailable;

    public bool TrySend(IReadOnlyList<byte[]> reports) => false;

    public void Dispose()
    {
    }
}

/// <summary>
/// Sends raw Micro input reports to the local VHF broker. The hot path uses a
/// persistent pipe and a one-byte broker acknowledgement; an absent broker is
/// detected immediately and put on a short retry backoff.
/// </summary>
public sealed class NamedPipeMicroReportTransport : IMicroReportTransport
{
    public const string DefaultPipeName =
        "AgentController.VirtualMicro.v1";
    public const byte SuccessAcknowledgement = 0x06;
    private static readonly byte[] BatchMagic = [0x41, 0x43, 0x4D, 0x31];
    private const int ConnectTimeoutMs = 1;
    private const int AcknowledgementTimeoutMs = 8;
    private const int RetryBackoffMs = 1_000;

    private readonly object _sync = new();
    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private long _retryAfter;
    private MicroTransportState _state = MicroTransportState.Unavailable;

    public NamedPipeMicroReportTransport(
        string pipeName = DefaultPipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    public MicroTransportState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public bool TrySend(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count == 0 || reports.Count > ushort.MaxValue)
        {
            return false;
        }

        foreach (var report in reports)
        {
            if (report is null || report.Length != MicroRpcCodec.ReportLength)
            {
                throw new ArgumentException(
                    "Every Micro report must contain exactly 64 bytes.",
                    nameof(reports));
            }
        }

        lock (_sync)
        {
            if (!EnsureConnected())
            {
                return false;
            }

            try
            {
                Span<byte> header = stackalloc byte[8];
                BatchMagic.CopyTo(header);
                BitConverter.TryWriteBytes(header[4..6], (ushort)reports.Count);
                _pipe!.Write(header);
                foreach (var report in reports)
                {
                    _pipe.Write(report);
                }

                _pipe.Flush();
                var response = new byte[1];
                using var acknowledgementTimeout =
                    new CancellationTokenSource(
                        AcknowledgementTimeoutMs);
                var responseLength = _pipe
                    .ReadAsync(
                        response,
                        acknowledgementTimeout.Token)
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
                if (
                    responseLength == 1 &&
                    response[0] == SuccessAcknowledgement)
                {
                    _state = MicroTransportState.Ready;
                    return true;
                }

                FailConnection();
                return false;
            }
            catch (IOException)
            {
                FailConnection();
                return false;
            }
            catch (ObjectDisposedException)
            {
                FailConnection();
                return false;
            }
            catch (OperationCanceledException)
            {
                FailConnection();
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _pipe?.Dispose();
            _pipe = null;
            _state = MicroTransportState.Unavailable;
        }
    }

    private bool EnsureConnected()
    {
        if (_pipe?.IsConnected == true)
        {
            return true;
        }

        if (Environment.TickCount64 < _retryAfter)
        {
            return false;
        }

        _pipe?.Dispose();
        _pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            _pipe.Connect(ConnectTimeoutMs);
            _pipe.ReadMode = PipeTransmissionMode.Byte;
            _state = MicroTransportState.Ready;
            return true;
        }
        catch (TimeoutException)
        {
            FailConnection();
            return false;
        }
        catch (IOException)
        {
            FailConnection();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            FailConnection(faulted: true);
            return false;
        }
    }

    private void FailConnection(bool faulted = false)
    {
        _pipe?.Dispose();
        _pipe = null;
        _retryAfter = Environment.TickCount64 + RetryBackoffMs;
        _state = faulted
            ? MicroTransportState.Faulted
            : MicroTransportState.Unavailable;
    }
}
