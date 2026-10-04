using CodexMicro.Codex;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using AgentController.MicroSurface.Wpf;
using AgentController.MicroSurface.Wpf.SoftwareControl;

namespace CodexMicro.Plugin;

internal sealed class KeypadWindowHost(MicroSurfaceController surface, Dispatcher dispatcher) : IDisposable
{
    private static readonly int SessionId = Process.GetCurrentProcess().SessionId;
    private static readonly string PipeName = $"CodexMicro.NativeKeypad.v2.{SessionId}";
    internal static readonly string MutexName = $"Local\\CodexMicro.NativeKeypad.v2.{SessionId}";
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;

    internal void Start() => _listener = ListenAsync();

    internal static async Task<JsonNode> ShowAsync(string? threadId, CancellationToken cancellationToken)
    {
        if (threadId is not null) JsonSupport.ThreadId(threadId);
        if (await TryActivateAsync(threadId, cancellationToken)) return JsonSupport.Node(new { shown = true });
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new IOException("Keypad executable unavailable"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--window");
        if (threadId is not null) { start.ArgumentList.Add("--thread"); start.ArgumentList.Add(threadId); }
        using var process = Process.Start(start) ?? throw new IOException("Cannot open keypad");
        return JsonSupport.Node(new { launched = true, processId = process.Id });
    }

    internal static async Task<bool> TryActivateAsync(string? threadId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(650));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSupport.Node(new { threadId }).ToJsonString().AsMemory(), timeout.Token);
            return await reader.ReadLineAsync(timeout.Token) == "ok";
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    private async Task ListenAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_lifetime.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                var text = await reader.ReadLineAsync(timeout.Token);
                if (text is null || text.Length > 1024) continue;
                var threadId = JsonNode.Parse(text)?.Text("threadId");
                if (threadId is not null) JsonSupport.ThreadId(threadId);
                await dispatcher.InvokeAsync(() =>
                {
                    if (threadId is not null) surface.SelectThread(threadId);
                    surface.Show();
                });
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync("ok".AsMemory(), timeout.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or System.Text.Json.JsonException or ArgumentException) { }
        }
    }

    public void Dispose() { _lifetime.Cancel(); }
}
