using Microsoft.Win32;

namespace CodexMicro.DesktopHost;

internal sealed class MicroStartupRegistration
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexMicroKeypad";

    internal bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string value &&
                    !string.IsNullOrWhiteSpace(value);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or
                    System.Security.SecurityException)
            {
                return false;
            }
        }
    }

    internal void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath) ??
            throw new InvalidOperationException(
                "Could not open the current-user startup registry key.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "Could not resolve the Codex Micro executable path.");
        }

        key.SetValue(
            ValueName,
            $"\"{executable}\"",
            RegistryValueKind.String);
    }
}
