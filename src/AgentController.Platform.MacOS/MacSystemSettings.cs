using System.Diagnostics;

namespace AgentController.Platform.MacOS;

public static class MacSystemSettings
{
    public static bool TryOpenPrivacySettings()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo("open")
            {
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(
                "x-apple.systempreferences:com.apple.preference.security?Privacy");
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
