using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CodexMicro.Desktop.Services;

internal static class CodexAppServerProcess
{
    private static readonly TimeSpan ExitTimeout =
        TimeSpan.FromMilliseconds(500);

    internal static async ValueTask StopAsync(
        Process process,
        bool started)
    {
        if (!started)
        {
            return;
        }

        try
        {
            process.StandardInput.Close();
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException)
        {
        }

        Task exit;
        try
        {
            exit = process.WaitForExitAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception)
        {
            return;
        }

        if (ReferenceEquals(
                await Task.WhenAny(exit, Task.Delay(ExitTimeout))
                    .ConfigureAwait(false),
                exit))
        {
            await exit.ConfigureAwait(false);
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                var killedExit = process.WaitForExitAsync();
                if (ReferenceEquals(
                        await Task.WhenAny(
                                killedExit,
                                Task.Delay(ExitTimeout))
                            .ConfigureAwait(false),
                        killedExit))
                {
                    await killedExit.ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception)
        {
        }
    }
}
