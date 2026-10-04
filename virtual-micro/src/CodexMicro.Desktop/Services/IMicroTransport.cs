using System.Diagnostics.CodeAnalysis;
using AgentController.MicroBroker;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

internal sealed record MicroControlContext(
    string? ThreadId,
    IReadOnlyDictionary<string, string> AgentThreads,
    CodexMicroLayoutSnapshot Layout,
    MicroProfileSnapshot Profile,
    long TargetVersion = 0);

internal interface IMicroTransport : IDisposable
{
    event EventHandler<string>? Log;
    event EventHandler<string>? StateChanged;
    event EventHandler<SlotLightingSnapshot>? SlotLightingObserved;
    bool IsReady { get; }
    bool CodexLinkObserved { get; }
    bool UsesSoftwareControl => false;
    Func<MicroControlContext>? CaptureContext { set { } }
    Action<string?>? ThreadOpened { set { } }
    Action<string, string?>? ServiceTierApplied { set { } }
    Func<string?, CancellationToken, Task<bool>>? ValidateTargetAsync { set { } }
    ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
    void StartConnecting();
    bool TryConnect([NotNullWhen(true)] out BrokerDriverInfo? info, out string error);
    Task<BrokerDriverInfo> RecoverCodexLinkAsync();
    Task<MicroSendResult> TapKeyAsync(string key);
    Task<MicroSendResult> SetKeyAsync(string key, bool pressed);
    Task<MicroSendResult> StepEncoderAsync(bool clockwise);
    Task<MicroSendResult> OpenCodexMicroSettingsAsync(CancellationToken cancellationToken = default);
    Task<MicroSendResult> SetJoystickStateAsync(double angle, double distance, string direction);
    Task<MicroSendResult> MoveJoystickAsync(double angle, double distance, string direction);
}
