using Microsoft.Win32;

namespace CodexMicro.DesktopHost;

internal sealed class MicroStartupRegistration(string valueName = "CodexMicroKeypad")
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(valueName) is string value &&
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
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "Could not resolve the Codex Micro executable path.");
        }

        key.SetValue(
            valueName,
            $"\"{executable}\" --background",
            RegistryValueKind.String);
    }
}
