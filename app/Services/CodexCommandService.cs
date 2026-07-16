using System.Diagnostics;
using CodexController.Models;
using CodexController.Native;

namespace CodexController.Services;

public sealed class CodexCommandService
{
    public bool IsCodexForeground => Win32Input.IsCodexForeground();

    public bool WakeCodex()
    {
        if (Win32Input.FocusCodexAndWait(timeoutMs: 650))
        {
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments =
                    @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App",
                UseShellExecute = true,
            });
        }
        catch
        {
            return false;
        }

        var deadline = Environment.TickCount64 + 10_000;
        while (Environment.TickCount64 < deadline)
        {
            if (Win32Input.FocusCodexAndWait(timeoutMs: 180))
            {
                return true;
            }

            Thread.Sleep(120);
        }

        return false;
    }

    public bool CanExecute(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return false;
        }

        if (!settings.OnlyWhenCodexForeground)
        {
            return true;
        }

        return Win32Input.IsCodexForeground();
    }

    public bool ExecuteShortcut(string shortcut, AppSettings settings)
    {
        if (!CanExecute(settings))
        {
            return false;
        }

        if (
            !settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            if (!Win32Input.FocusCodexAndWait())
            {
                return false;
            }
        }

        return Win32Input.SendShortcut(shortcut);
    }

    public async Task<bool> StepModelAsync(
        int steps,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (steps == 0)
        {
            return false;
        }

        if (!ExecuteShortcut(settings.ModelPickerShortcut, settings))
        {
            return false;
        }

        try
        {
            await Task.Delay(180, cancellationToken).ConfigureAwait(false);
            var navigationKey = steps > 0 ? (ushort)0x28 : (ushort)0x26;
            for (var index = 0; index < Math.Abs(steps); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Win32Input.SendKey(navigationKey);
                await Task.Delay(55, cancellationToken).ConfigureAwait(false);
            }

            // Let the picker finish updating its preview before committing once.
            await Task.Delay(160, cancellationToken).ConfigureAwait(false);
            Win32Input.SendKey(0x0D);
            return true;
        }
        catch (OperationCanceledException)
        {
            Win32Input.SendKey(0x1B);
            return false;
        }
    }

    public static bool OpenThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"codex://threads/{threadId}",
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenCodexSettings()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "codex://settings",
            UseShellExecute = true,
        });
    }

    public static void OpenUltimateSoftware()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://app.8bitdo.com/Ultimate-Software-V2/",
            UseShellExecute = true,
        });
    }

    public static void OpenCodexKeyboardShortcuts()
    {
        if (!Win32Input.FocusCodex())
        {
            OpenCodexSettings();
            return;
        }

        Thread.Sleep(90);
        _ = Win32Input.SendShortcut("Ctrl+Shift+Slash");
    }
}
