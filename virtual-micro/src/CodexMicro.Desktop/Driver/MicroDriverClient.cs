using System.IO;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Driver;

internal sealed class VirtualMicroDriverClient : IDisposable
{
    private IDisposable? _lifetime;
    private Func<bool>? _isConnected;
    private Func<IReadOnlyList<byte[]>, MicroSendResult>? _submit;
    private Func<VhfKeyboardKey, bool, MicroSendResult>? _tapKeyboard;
    private Func<DriverOutputReport?>? _readOutput;

    public bool SupportsDialogKeyboard { get; private set; }

    public bool IsConnected => _isConnected?.Invoke() == true;

    public DriverInfo Connect()
    {
        DisposeTransport();
        var vhf = new VhfMicroDriverClient();
        try
        {
            var info = vhf.Connect();
            Bind(
                vhf,
                () => vhf.IsConnected,
                vhf.Submit,
                vhf.TapKeyboardKey,
                vhf.TryReadOutput);
            SupportsDialogKeyboard =
                (info.Flags & DriverContract.InfoFlagDialogKeyboard) != 0;
            return info;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or
                InvalidDataException or
                IOException)
        {
            vhf.Dispose();
            throw new InvalidOperationException(
                "The Microsoft VHF Codex Micro device was not found. " +
                exception.Message,
                exception);
        }
    }

    public MicroSendResult Submit(IReadOnlyList<byte[]> reports) =>
        _submit?.Invoke(reports) ??
        throw new InvalidOperationException("Codex Micro HID is not connected.");

    public MicroSendResult TapKeyboardKey(VhfKeyboardKey key, bool shift) =>
        _tapKeyboard?.Invoke(key, shift) ??
        throw new InvalidOperationException("Codex Micro VHF keyboard is not connected.");

    public DriverOutputReport? TryReadOutput()
    {
        var readOutput = _readOutput ??
            throw new InvalidOperationException(
                "Codex Micro HID is not connected.");
        return readOutput();
    }

    public void Dispose() => DisposeTransport();

    private void Bind(
        IDisposable lifetime,
        Func<bool> isConnected,
        Func<IReadOnlyList<byte[]>, MicroSendResult> submit,
        Func<VhfKeyboardKey, bool, MicroSendResult> tapKeyboard,
        Func<DriverOutputReport?> readOutput)
    {
        _lifetime = lifetime;
        _isConnected = isConnected;
        _submit = submit;
        _tapKeyboard = tapKeyboard;
        _readOutput = readOutput;
    }

    private void DisposeTransport()
    {
        _lifetime?.Dispose();
        _lifetime = null;
        _isConnected = null;
        _submit = null;
        _tapKeyboard = null;
        _readOutput = null;
        SupportsDialogKeyboard = false;
    }
}
