using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace DeepSeekKeypad.OneClick;

internal static class Program
{
    private const string SingleInstanceName =
        "Local\\DeepSeekKeypad.OneClick.7F795D37-3DBE-4F64-AC22-D1D1907AE572";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--verify-only", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                OneClickPayload.Open(Environment.ProcessPath!).VerifySha256();
                Environment.ExitCode = 0;
            }
            catch
            {
                Environment.ExitCode = 2;
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceName,
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Deepseek Harness Keypad 安装程序已经在运行。",
                "Deepseek Harness Keypad",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var options = InstallerOptions.Parse(args);
        using var form = new InstallerForm(options);
        Application.Run(form);
    }
}

internal sealed record InstallerOptions(
    bool AutoStart,
    bool LaunchAfterInstall,
    bool Quiet)
{
    internal static InstallerOptions Parse(IEnumerable<string> args)
    {
        var values = args.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new(
            AutoStart: !values.Contains("--no-autostart"),
            LaunchAfterInstall: !values.Contains("--no-launch"),
            Quiet: values.Contains("--quiet"));
    }
}

internal sealed class InstallerForm : Form
{
    private const string ProductId = "deepseek-harness-keypad";
    private const string ReleaseVersion = "0.2.5";
    private const string InstallDirectoryName = "Deepseek Harness Keypad";
    private const string ExecutableName = "CodexMicro.exe";
    private const string MarkerName = ".deepseek-harness-keypad.install.json";
    private const string RunValueName = "CodexMicroKeypad";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepseekHarnessKeypad";
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly InstallerOptions _options;
    private readonly Label _status;
    private readonly ProgressBar _progress;
    private readonly Button _close;

    internal InstallerForm(InstallerOptions options)
    {
        _options = options;
        Text = $"Deepseek Harness Keypad v{ReleaseVersion} 一键安装";
        ClientSize = new(560, 178);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(24, 22),
            Text = $"Deepseek Harness Keypad v{ReleaseVersion}",
        };
        _status = new Label
        {
            AutoEllipsis = true,
            Location = new Point(24, 57),
            Size = new Size(512, 44),
            Text = "正在检查安装包……",
        };
        _progress = new ProgressBar
        {
            Location = new Point(24, 108),
            MarqueeAnimationSpeed = 24,
            Size = new Size(512, 15),
            Style = ProgressBarStyle.Marquee,
        };
        _close = new Button
        {
            DialogResult = DialogResult.Cancel,
            Enabled = false,
            Location = new Point(436, 139),
            Size = new Size(100, 28),
            Text = "关闭",
        };
        _close.Click += (_, _) => Close();
        Controls.AddRange([title, _status, _progress, _close]);
        Shown += async (_, _) => await InstallAsync();
        FormClosing += (_, eventArgs) =>
        {
            if (!_close.Enabled)
            {
                eventArgs.Cancel = true;
            }
        };
    }

    private async Task InstallAsync()
    {
        try
        {
            EnsureSupportedWindows();
            var installRoot = GetInstallRoot();
            await StopInstalledProcessesAsync(installRoot);
            SetStatus("正在校验内置 Full 载荷……");
            await Task.Run(() => InstallPayload(installRoot));
            SetStatus("正在注册后台自启动和卸载入口……");
            RegisterInstallation(installRoot, _options.AutoStart);
            SetStatus(
                "安装完成。首次点击 DeepSeek 键时会导入内置的官方 DSH；已有 DSH 可继续直接连接。");
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 100;
            _close.Enabled = true;

            if (_options.LaunchAfterInstall)
            {
                LaunchInstalledApplication(installRoot);
                await Task.Delay(900);
                Close();
            }
            else if (_options.Quiet)
            {
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            SetFailure("安装已取消；现有文件没有被修改。");
        }
        catch (Exception exception)
        {
            SetFailure($"安装失败：{exception.Message}");
        }
    }

    private void SetStatus(string value) => _status.Text = value;

    private void SetFailure(string value)
    {
        _status.Text = value;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = 0;
        _close.Enabled = true;
        Environment.ExitCode = 1;
        if (_options.Quiet)
        {
            Close();
            return;
        }
        MessageBox.Show(
            this,
            value,
            "Deepseek Harness Keypad",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void EnsureSupportedWindows()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException(
                "需要 Windows 10 版本 2004（内部版本 19041）或更高版本。");
        }
        if (RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "当前 oneclick 包只支持 Windows x64。");
        }
    }

    private static string GetInstallRoot()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("无法确定当前用户的 LocalAppData 目录。");
        }
        return Path.GetFullPath(Path.Combine(
            localAppData,
            "Programs",
            InstallDirectoryName));
    }

    private async Task StopInstalledProcessesAsync(string installRoot)
    {
        var executable = Path.Combine(installRoot, ExecutableName);
        var matches = Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(ExecutableName))
            .Where(process => IsProcessAtPath(process, executable))
            .ToArray();
        if (matches.Length == 0)
        {
            return;
        }
        if (_options.Quiet)
        {
            foreach (var process in matches)
            {
                process.Dispose();
            }
            throw new InvalidOperationException(
                "当前安装目录中的小键盘仍在运行；请关闭后重试静默安装。");
        }

        var choice = MessageBox.Show(
            this,
            "需要先关闭当前安装目录中运行的小键盘和 Bridge，是否继续？\n\n其他位置的 DSH 与 Qwen 语音服务不会被关闭。",
            "更新 Deepseek Harness Keypad",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (choice != DialogResult.Yes)
        {
            throw new OperationCanceledException();
        }

        foreach (var process in matches)
        {
            using (process)
            {
                try
                {
                    // Only stop product executables at the validated install
                    // path. DSH and keypad-owned Qwen may be child processes
                    // and must remain available across an app repair/update.
                    process.Kill(entireProcessTree: false);
                    await process.WaitForExitAsync().WaitAsync(
                        TimeSpan.FromSeconds(8));
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                        System.ComponentModel.Win32Exception)
                {
                    // The process may have exited between enumeration and stop.
                }
            }
        }
    }

    private static bool IsProcessAtPath(Process process, string expectedPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(process.MainModule?.FileName ?? string.Empty),
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or
                NotSupportedException)
        {
            return false;
        }
    }

    private static void InstallPayload(string installRoot)
    {
        var packagePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            throw new InvalidOperationException("无法读取 oneclick 安装包路径。");
        }
        var payload = OneClickPayload.Open(packagePath);
        payload.VerifySha256();

        var parent = Directory.GetParent(installRoot)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException("无法确定安装目录的父目录。");
        }
        Directory.CreateDirectory(parent);
        EnsureInstallDirectoryIsOwned(installRoot);

        var suffix = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(parent, $".{InstallDirectoryName}.install-{suffix}");
        var backup = Path.Combine(parent, $".{InstallDirectoryName}.backup-{suffix}");
        var movedExisting = false;
        var movedStaging = false;
        try
        {
            Directory.CreateDirectory(staging);
            payload.ExtractAndVerify(staging, ProductId, ReleaseVersion);

            var marker = ReadMarker(Path.Combine(staging, MarkerName));
            if (!string.Equals(marker.ProductId, ProductId, StringComparison.Ordinal) ||
                !string.Equals(marker.Version, ReleaseVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("安装标记与 oneclick 版本不一致。");
            }
            if (!File.Exists(Path.Combine(staging, ExecutableName)))
            {
                throw new InvalidDataException("Full 载荷缺少 CodexMicro.exe。");
            }

            if (Directory.Exists(installRoot))
            {
                Directory.Move(installRoot, backup);
                movedExisting = true;
            }
            Directory.Move(staging, installRoot);
            movedStaging = true;
            if (movedExisting)
            {
                DeleteOwnedDirectory(backup);
                movedExisting = false;
            }
        }
        catch
        {
            if (movedStaging && Directory.Exists(installRoot))
            {
                DeleteOwnedDirectory(installRoot);
            }
            if (movedExisting && Directory.Exists(backup))
            {
                Directory.Move(backup, installRoot);
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private static void EnsureInstallDirectoryIsOwned(string installRoot)
    {
        if (!Directory.Exists(installRoot))
        {
            return;
        }
        if (!Directory.EnumerateFileSystemEntries(installRoot).Any())
        {
            Directory.Delete(installRoot);
            return;
        }
        var markerPath = Path.Combine(installRoot, MarkerName);
        if (!File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                $"目标目录已包含非本安装器管理的文件：{installRoot}");
        }
        var marker = ReadMarker(markerPath);
        if (!string.Equals(marker.ProductId, ProductId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"目标目录属于另一个产品：{installRoot}");
        }
    }

    private static InstallMarker ReadMarker(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<InstallMarker>(stream) ??
            throw new InvalidDataException("安装标记无效。");
    }

    private static void DeleteOwnedDirectory(string path)
    {
        var markerPath = Path.Combine(path, MarkerName);
        if (!File.Exists(markerPath))
        {
            throw new InvalidOperationException(
                $"拒绝删除缺少产品标记的目录：{path}");
        }
        var marker = ReadMarker(markerPath);
        if (!string.Equals(marker.ProductId, ProductId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"拒绝删除不属于本产品的目录：{path}");
        }
        Directory.Delete(path, recursive: true);
    }

    private static void RegisterInstallation(string installRoot, bool autoStart)
    {
        var executable = Path.Combine(installRoot, ExecutableName);
        if (autoStart)
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath) ??
                throw new InvalidOperationException("无法写入当前用户的开机启动项。");
            runKey.SetValue(
                RunValueName,
                $"\"{executable}\" --background",
                RegistryValueKind.String);
        }
        else
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(
                RunKeyPath,
                writable: true);
            if (runKey?.GetValue(RunValueName) is string value &&
                value.Contains(executable, StringComparison.OrdinalIgnoreCase))
            {
                runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }

        var shortcutPath = string.Empty;
        try
        {
            shortcutPath = CreateStartMenuShortcut(executable, installRoot);
        }
        catch (Exception exception) when (
            exception is COMException or InvalidOperationException or
                UnauthorizedAccessException)
        {
            // Enterprise policy may disable Windows Script Host. Installation
            // and tray startup remain usable without a Start menu shortcut.
        }
        using var uninstallKey = Registry.CurrentUser.CreateSubKey(
            UninstallKeyPath) ?? throw new InvalidOperationException(
                "无法写入当前用户的卸载信息。");
        var uninstallScript = Path.Combine(
            installRoot,
            "Uninstall-DeepseekHarnessKeypad.ps1");
        var windowsPowerShell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        uninstallKey.SetValue(
            "DisplayName",
            $"Deepseek Harness Keypad {ReleaseVersion}");
        uninstallKey.SetValue("DisplayVersion", ReleaseVersion);
        uninstallKey.SetValue("Publisher", "AgentController");
        uninstallKey.SetValue("InstallLocation", installRoot);
        uninstallKey.SetValue("DisplayIcon", executable);
        uninstallKey.SetValue(
            "UninstallString",
            $"\"{windowsPowerShell}\" -NoProfile -ExecutionPolicy Bypass -File \"{uninstallScript}\"");
        uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
        uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        uninstallKey.SetValue(
            "EstimatedSize",
            CalculateInstalledKiB(installRoot),
            RegistryValueKind.DWord);
        uninstallKey.SetValue("StartMenuShortcut", shortcutPath);
    }

    private static int CalculateInstalledKiB(string root)
    {
        var bytes = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
        return (int)Math.Min(int.MaxValue, (bytes + 1023) / 1024);
    }

    private static string CreateStartMenuShortcut(
        string executable,
        string installRoot)
    {
        var startMenu = Environment.GetFolderPath(
            Environment.SpecialFolder.StartMenu);
        var programs = Path.Combine(startMenu, "Programs");
        Directory.CreateDirectory(programs);
        var shortcutPath = Path.Combine(programs, "Deepseek Harness Keypad.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ??
            throw new InvalidOperationException("Windows Script Host 不可用，无法创建开始菜单快捷方式。");
        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType) ??
                throw new InvalidOperationException("无法创建 Windows Script Host 实例。");
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                throw new InvalidOperationException("无法创建开始菜单快捷方式。");
            }
            var type = shortcut.GetType();
            type.InvokeMember(
                "TargetPath",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                [executable]);
            type.InvokeMember(
                "WorkingDirectory",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                [installRoot]);
            type.InvokeMember(
                "IconLocation",
                System.Reflection.BindingFlags.SetProperty,
                null,
                shortcut,
                [$"{executable},0"]);
            type.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shortcut,
                null);
            return shortcutPath;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void LaunchInstalledApplication(string installRoot)
    {
        var executable = Path.Combine(installRoot, ExecutableName);
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = installRoot,
            UseShellExecute = true,
        });
    }
}

internal sealed record InstallMarker(string ProductId, string Version);

internal sealed record PayloadManifest(
    int SchemaVersion,
    string ProductId,
    string Version,
    IReadOnlyList<PayloadFile> Files);

internal sealed record PayloadFile(string Path, long Size, string Sha256);

internal sealed class OneClickPayload
{
    private const int FooterSize = 64;
    private const string FooterMagic = "DSHKP_ONECLICK_1";
    private const string ManifestName = "oneclick-manifest.json";

    private readonly string _packagePath;
    private readonly long _offset;
    private readonly long _length;
    private readonly byte[] _sha256;

    private OneClickPayload(
        string packagePath,
        long offset,
        long length,
        byte[] sha256)
    {
        _packagePath = packagePath;
        _offset = offset;
        _length = length;
        _sha256 = sha256;
    }

    internal static OneClickPayload Open(string packagePath)
    {
        using var stream = File.Open(
            packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length < FooterSize)
        {
            throw new InvalidDataException("oneclick 安装包缺少 Full 载荷。");
        }
        stream.Position = stream.Length - FooterSize;
        Span<byte> footer = stackalloc byte[FooterSize];
        stream.ReadExactly(footer);
        if (!footer[..16].SequenceEqual(Encoding.ASCII.GetBytes(FooterMagic)))
        {
            throw new InvalidDataException("oneclick 安装包尾标记无效。");
        }
        var offset = BinaryPrimitives.ReadInt64LittleEndian(footer[16..24]);
        var length = BinaryPrimitives.ReadInt64LittleEndian(footer[24..32]);
        if (offset < 0 || length <= 0 ||
            offset + length + FooterSize != stream.Length)
        {
            throw new InvalidDataException("oneclick Full 载荷边界无效。");
        }
        return new(
            Path.GetFullPath(packagePath),
            offset,
            length,
            footer[32..64].ToArray());
    }

    internal void VerifySha256()
    {
        using var stream = OpenSegment();
        var actual = SHA256.HashData(stream);
        if (!CryptographicOperations.FixedTimeEquals(actual, _sha256))
        {
            throw new InvalidDataException("oneclick Full 载荷 SHA-256 校验失败。");
        }
    }

    internal void ExtractAndVerify(
        string destination,
        string expectedProductId,
        string expectedVersion)
    {
        using var segment = OpenSegment();
        using var archive = new ZipArchive(
            segment,
            ZipArchiveMode.Read,
            leaveOpen: false,
            entryNameEncoding: Encoding.UTF8);
        var manifestEntry = archive.GetEntry(ManifestName) ??
            throw new InvalidDataException("Full 载荷缺少文件清单。");
        if (manifestEntry.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException("Full 载荷文件清单异常过大。");
        }
        PayloadManifest manifest;
        using (var manifestStream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<PayloadManifest>(
                manifestStream) ?? throw new InvalidDataException(
                    "Full 载荷文件清单无效。");
        }
        if (manifest.SchemaVersion != 1 ||
            !string.Equals(
                manifest.ProductId,
                expectedProductId,
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Version,
                expectedVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Full 载荷产品或版本不匹配。");
        }

        var files = manifest.Files.ToDictionary(
            file => NormalizeArchivePath(file.Path),
            StringComparer.OrdinalIgnoreCase);
        var archiveFiles = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                !string.Equals(
                    NormalizeArchivePath(entry.FullName),
                    ManifestName,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var archivePaths = archiveFiles
            .Select(entry => NormalizeArchivePath(entry.FullName))
            .ToArray();
        if (archiveFiles.Length != files.Count ||
            archivePaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                archivePaths.Length ||
            archiveFiles.Any(entry =>
                !files.ContainsKey(NormalizeArchivePath(entry.FullName))))
        {
            throw new InvalidDataException("Full 载荷包含未列入清单的文件。");
        }

        EnsureDiskSpace(destination, manifest.Files.Sum(file => file.Size));
        foreach (var entry in archiveFiles)
        {
            var relative = NormalizeArchivePath(entry.FullName);
            var expected = files[relative];
            if (expected.Size < 0 || entry.Length != expected.Size)
            {
                throw new InvalidDataException($"Full 载荷文件大小不符：{relative}");
            }
            var target = ResolveSafeTarget(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = File.Open(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
            {
                output.Write(buffer, 0, read);
                hash.AppendData(buffer, 0, read);
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(
                    actual,
                    expected.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Full 载荷文件校验失败：{relative}");
            }
        }
    }

    private SegmentReadStream OpenSegment()
    {
        var stream = File.Open(
            _packagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return new(stream, _offset, _length);
    }

    private static string NormalizeArchivePath(string value)
    {
        var normalized = value.Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Split('/').Any(part =>
                part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Full 载荷路径不安全：{value}");
        }
        return normalized;
    }

    private static string ResolveSafeTarget(string destination, string relative)
    {
        var root = Path.GetFullPath(destination).TrimEnd(
            Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(
            destination,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Full 载荷路径越界：{relative}");
        }
        return target;
    }

    private static void EnsureDiskSpace(string destination, long requiredBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(destination));
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("无法确定安装磁盘。");
        }
        var available = new DriveInfo(root).AvailableFreeSpace;
        var margin = Math.Max(512L * 1024 * 1024, requiredBytes / 5);
        if (requiredBytes < 0 || available < requiredBytes + margin)
        {
            throw new IOException(
                $"安装磁盘空间不足；至少还需要 {(requiredBytes + margin) / 1024 / 1024:N0} MiB。");
        }
    }
}

internal sealed class SegmentReadStream : Stream
{
    private readonly FileStream _inner;
    private readonly long _offset;
    private readonly long _length;
    private long _position;

    internal SegmentReadStream(FileStream inner, long offset, long length)
    {
        _inner = inner;
        _offset = offset;
        _length = length;
        _inner.Position = offset;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }
        var read = _inner.Read(buffer, offset, (int)Math.Min(count, remaining));
        _position += read;
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _length - _position;
        if (remaining <= 0)
        {
            return 0;
        }
        var read = _inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        if (target < 0 || target > _length)
        {
            throw new IOException("Attempted to seek outside the oneclick payload.");
        }
        _inner.Position = _offset + target;
        _position = target;
        return _position;
    }

    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
