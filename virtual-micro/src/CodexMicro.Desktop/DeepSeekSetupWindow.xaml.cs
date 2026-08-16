using System.ComponentModel;
using System.Windows;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

internal enum DeepSeekSetupWindowChoice
{
    None,
    ExistingHarness,
    ManagedReady,
    RestartRequired,
}

public partial class DeepSeekSetupWindow : Window
{
    private readonly Func<
        IProgress<DeepSeekSetupProgress>,
        CancellationToken,
        Task<DeepSeekSetupResult>> _configureManaged;
    private readonly Action<DeepSeekSetupProgress>? _surfaceProgress;
    private readonly bool _english;
    private CancellationTokenSource? _setupCancellation;
    private bool _running;
    private bool _allowClose;

    internal DeepSeekSetupWindow(
        DeepSeekEndpointProbe probe,
        Func<
            IProgress<DeepSeekSetupProgress>,
            CancellationToken,
            Task<DeepSeekSetupResult>> configureManaged,
        Action<DeepSeekSetupProgress>? surfaceProgress = null,
        bool english = false)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _configureManaged = configureManaged ??
            throw new ArgumentNullException(nameof(configureManaged));
        _surfaceProgress = surfaceProgress;
        _english = english;
        InitializeComponent();
        ApplyLanguage();
        DetectionDetailText.Text = BuildDetectionMessage(probe);
        if (probe.WebReachable && !probe.BridgeReachable)
        {
            DetectionTitleText.Text = _english
                ? "Harness found · bridge missing"
                : "已发现 Harness · 缺少桥接";
        }
        else
        {
            DetectionTitleText.Text = _english
                ? "First-time setup required"
                : "需要首次配置";
        }
    }

    internal DeepSeekSetupWindowChoice Choice { get; private set; }

    internal DeepSeekSetupResult? ManagedResult { get; private set; }

    private string BuildDetectionMessage(DeepSeekEndpointProbe probe)
    {
        if (_english)
        {
            return probe.Message;
        }

        if (probe.BridgeReachable)
        {
            return $"在 {probe.BaseUri} 找到可用的 DeepSeek Harness 与 Micro 桥接。";
        }
        if (probe.WebReachable)
        {
            return $"在 {probe.BaseUri} 找到 DeepSeek Harness，但还没有 Micro 桥接。";
        }
        return "已检查保存的地址和官方默认地址 http://127.0.0.1:3080/，尚未发现可用的 Harness。";
    }

    private void ApplyLanguage()
    {
        if (!_english)
        {
            return;
        }

        Title = "Set up DeepSeek Harness";
        HeadingText.Text = "Make DeepSeek ready";
        IntroText.Text =
            "The first click looks for DSH. Install it in dedicated WSL, or use a Harness you already run on Windows or WSL.";
        ManagedSetupButton.Content = "Install DSH in dedicated WSL (recommended)";
        ManagedSetupDetailText.Text =
            "Installs a compatible DSH runtime and standalone Bridge without changing your existing DSH.";
        ExistingSetupButton.Content = "Use my existing DSH";
        ExistingSetupDetailText.Text =
            "Locate a Windows or WSL Harness yourself, keep its version and launch method, then configure its control address and standalone plugin.";
        PrivacyText.Text =
            "Your API key remains in the official DeepSeek UI; this app does not read it.";
        CancelButton.Content = "Cancel";
    }

    private async void ManagedSetupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        ManagedSetupButton.IsEnabled = false;
        ExistingSetupButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        CancelButton.Content = _english ? "Cancel setup" : "取消配置";
        _setupCancellation = new CancellationTokenSource();
        var progress = new Progress<DeepSeekSetupProgress>(value =>
        {
            ApplyProgress(value);
            _surfaceProgress?.Invoke(value);
        });

        try
        {
            ManagedResult = await _configureManaged(
                progress,
                _setupCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            ManagedResult = new(
                DeepSeekSetupDisposition.Cancelled,
                _english
                    ? "Automatic setup was cancelled; no setup was marked complete."
                    : "已取消自动配置；程序没有把配置标记为完成。",
                Math.Max(1, (int)SetupProgressBar.Value));
        }
        finally
        {
            _setupCancellation.Dispose();
            _setupCancellation = null;
            _running = false;
        }

        switch (ManagedResult.Disposition)
        {
            case DeepSeekSetupDisposition.ManagedReady:
                Choice = DeepSeekSetupWindowChoice.ManagedReady;
                _allowClose = true;
                Close();
                return;
            case DeepSeekSetupDisposition.RestartRequired:
                Choice = DeepSeekSetupWindowChoice.RestartRequired;
                ShowResult(ManagedResult, retryAllowed: false);
                return;
            case DeepSeekSetupDisposition.Cancelled:
            case DeepSeekSetupDisposition.Failed:
                ShowResult(ManagedResult, retryAllowed: true);
                return;
        }
    }

    private void ExistingSetupButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_running)
        {
            return;
        }

        Choice = DeepSeekSetupWindowChoice.ExistingHarness;
        _allowClose = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _setupCancellation?.Cancel();
            CancelButton.IsEnabled = false;
            ProgressDetailText.Text = _english
                ? "Cancelling safely…"
                : "正在安全取消…";
            return;
        }

        _allowClose = true;
        Close();
    }

    private void ApplyProgress(DeepSeekSetupProgress value)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        SetupProgressBar.Maximum = value.TotalSteps;
        SetupProgressBar.Value = value.Step;
        ProgressFractionText.Text = $"{value.Step}/{value.TotalSteps}";
        ProgressTitleText.Text = value.Title;
        ProgressDetailText.Text = value.Message;
    }

    private void ShowResult(
        DeepSeekSetupResult result,
        bool retryAllowed)
    {
        ApplyProgress(new(
            result.Step,
            result.TotalSteps,
            result.Disposition == DeepSeekSetupDisposition.RestartRequired
                ? _english ? "Windows restart required" : "需要重启 Windows"
                : _english ? "Setup needs attention" : "配置需要处理",
            result.Message));
        ManagedSetupButton.IsEnabled = retryAllowed;
        ManagedSetupButton.Content = retryAllowed
            ? _english ? "Retry dedicated WSL install" : "重试专用 WSL 安装"
            : _english ? "Continue after restart" : "重启后继续";
        ExistingSetupButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
        CancelButton.Content = _english ? "Close" : "关闭";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_running && !_allowClose)
        {
            e.Cancel = true;
            _setupCancellation?.Cancel();
            CancelButton.IsEnabled = false;
            ProgressDetailText.Text = _english
                ? "Cancelling safely…"
                : "正在安全取消…";
        }
    }
}
