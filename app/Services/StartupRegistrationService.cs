using Microsoft.Win32;

namespace CodexController.Services;

public sealed class StartupRegistrationService
{
    private const string StartupRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "AgentController";
    private const string LegacyHyphenatedValueName = "agent-controller";
    private const string LegacyCodexValueName = "CodexController";

    public void Update(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            StartupRegistryPath,
            writable: true);
        if (key is null)
        {
            return;
        }

        if (!enabled)
        {
            key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            key.DeleteValue(
                LegacyHyphenatedValueName,
                throwOnMissingValue: false);
            key.DeleteValue(
                LegacyCodexValueName,
                throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return;
        }

        key.SetValue(StartupValueName, $"\"{executable}\" --background");
        key.DeleteValue(
            LegacyHyphenatedValueName,
            throwOnMissingValue: false);
        key.DeleteValue(
            LegacyCodexValueName,
            throwOnMissingValue: false);
    }
}
