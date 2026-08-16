using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

public partial class MicroVoiceSettingsWindow : Window
{
    private sealed record ProviderChoice(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record StartModeChoice(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly MicroLocalization _localization;
    private readonly MicroProfileSettings _profileSettings;
    private readonly MicroVoiceInputService _voiceInput;
    private bool _syncing;
    private bool _busy;

    internal MicroVoiceSettingsWindow(
        MicroLocalization localization,
        MicroProfileSettings profileSettings,
        MicroVoiceInputService voiceInput)
    {
        _localization = localization ??
            throw new ArgumentNullException(nameof(localization));
        _profileSettings = profileSettings ??
            throw new ArgumentNullException(nameof(profileSettings));
        _voiceInput = voiceInput ??
            throw new ArgumentNullException(nameof(voiceInput));
        InitializeComponent();
        _localization.LanguageChanged += Localization_LanguageChanged;
        Closed += Window_Closed;
        RefreshPresentation();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        RefreshPresentation();

    private void RefreshPresentation()
    {
        var english = _localization.IsEnglish;
        var settings = _profileSettings.Current.VoiceSettings;
        _syncing = true;
        try
        {
            var choices = new[]
            {
                new ProviderChoice(
                    MicroVoiceProviders.System,
                    english ? "Windows speech" : "Windows 系统识别"),
                new ProviderChoice(
                    MicroVoiceProviders.LocalQwen,
                    english ? "Local streaming Qwen" : "本地流式 Qwen"),
                new ProviderChoice(
                    MicroVoiceProviders.RemoteWebSocket,
                    english ? "Remote streaming API" : "远程流式 API"),
            };
            ProviderCombo.ItemsSource = choices;
            ProviderCombo.SelectedItem = choices.First(choice =>
                choice.Id == settings.Provider);
            var startModes = new[]
            {
                new StartModeChoice(
                    MicroLocalVoiceStartModes.OnDemand,
                    english ? "Start on first use" : "首次使用时启动"),
                new StartModeChoice(
                    MicroLocalVoiceStartModes.KeypadStart,
                    english ? "Start with keypad" : "随小键盘启动"),
                new StartModeChoice(
                    MicroLocalVoiceStartModes.Manual,
                    english ? "Detect only (manual)" : "仅检测（手动启动）"),
            };
            LocalStartModeCombo.ItemsSource = startModes;
            LocalStartModeCombo.SelectedItem = startModes.First(choice =>
                choice.Id == settings.LocalStartMode);
            LanguageTextBox.Text = settings.Language;
            AutoSubmitToggle.IsChecked = settings.AutoSubmit;
            LocalUrlTextBox.Text = settings.LocalStreamUrl;
            LocalModelTextBox.Text = settings.LocalModel;
            LocalHealthUrlTextBox.Text = settings.LocalHealthUrl;
            LocalLauncherTextBox.Text = settings.LocalLauncherPath;
            LocalWorkingDirectoryTextBox.Text =
                settings.LocalWorkingDirectory;
            LocalDistributionTextBox.Text = settings.LocalDistribution;
            LocalPythonPathTextBox.Text = settings.LocalPythonPath;
            LocalReadyTimeoutTextBox.Text =
                settings.LocalReadyTimeoutSeconds.ToString();
            LocalStopWithKeypadToggle.IsChecked =
                settings.LocalStopWithKeypad;
            RemoteUrlTextBox.Text = settings.RemoteUrl;
            RemoteModelTextBox.Text = settings.RemoteModel;
            CredentialPasswordBox.Password = string.Empty;
        }
        finally
        {
            _syncing = false;
        }

        Title = english
            ? "Codex Micro · Voice input"
            : "Codex Micro · 语音输入";
        HeadingText.Text = english ? "Voice input" : "语音输入";
        OwnerBadgeText.Text = english ? "KEYPAD LOCAL" : "小键盘本地";
        IntroText.Text = english
            ? "The keypad owns the microphone, recognition service, and keys. DeepSeek receives only final text."
            : "麦克风、识别服务和密钥都由此小键盘持有；DeepSeek 只接收最终文字。";
        GeneralHeadingText.Text = english ? "Recognition" : "识别方式";
        ProviderLabelText.Text = english ? "Provider" : "提供商";
        LanguageLabelText.Text = english ? "Language (optional)" : "语言（可选）";
        AutoSubmitToggle.Content = english
            ? "Submit after recognition"
            : "识别完成后自动发送";
        SystemTitleText.Text = english
            ? "Windows speech"
            : "Windows 系统识别";
        SystemDetailText.Text = english
            ? "The keypad calls Windows Speech directly; the DeepSeek page never requests microphone permission."
            : "小键盘进程直接调用 Windows Speech；DeepSeek 页面不会请求麦克风权限。";
        LocalTitleText.Text = english
            ? "Local streaming Qwen ASR"
            : "本地流式 Qwen ASR";
        LocalDetailText.Text = english
            ? "The keypad detects and starts the service, then sends mono 16 kHz PCM directly."
            : "服务由小键盘检测和启动；小键盘直接发送 16 kHz 单声道 PCM。";
        LocalUrlLabelText.Text = english ? "Streaming URL" : "流式地址";
        LocalModelLabelText.Text = english ? "Model" : "模型";
        UseLocalExampleButton.Content = english
            ? "Use local example"
            : "使用本机示例";
        LocalStartModeLabelText.Text = english ? "Start mode" : "启动方式";
        LocalHealthUrlLabelText.Text = english
            ? "Health URL"
            : "健康检查地址";
        LocalLauncherLabelText.Text = english
            ? "Launcher script"
            : "启动脚本";
        LocalWorkingDirectoryLabelText.Text = english
            ? "Working directory"
            : "工作目录";
        LocalDistributionLabelText.Text = english
            ? "WSL distribution"
            : "WSL 发行版";
        LocalPythonPathLabelText.Text = english
            ? "WSL Python (optional)"
            : "WSL Python（可选）";
        LocalReadyTimeoutLabelText.Text = english
            ? "Ready timeout (seconds)"
            : "就绪超时（秒）";
        LocalStopWithKeypadToggle.Content = english
            ? "Stop the keypad-started service when the keypad exits"
            : "退出小键盘时停止由它启动的服务";
        LocalPathHintText.Text = english
            ? "Paths support {AppDir}, {LocalAppData}, and relative values; no machine-specific install directory is stored."
            : "路径支持 {AppDir}、{LocalAppData} 和相对路径；不会保存写死的本机安装目录。";
        RemoteTitleText.Text = english
            ? "Remote streaming API"
            : "远程流式 API";
        RemoteDetailText.Text = english
            ? "Public services must use wss://. Audio is sent directly by the keypad."
            : "公网服务必须使用 wss://；音频由小键盘直接发送。";
        RemoteUrlLabelText.Text = english ? "WebSocket URL" : "WebSocket 地址";
        RemoteModelLabelText.Text = english ? "Model (optional)" : "模型（可选）";
        CredentialTitleText.Text = "API Key";
        ClearCredentialButton.Content = english ? "Clear" : "清除";
        ProtocolNoteText.Text = english
            ? "Streaming protocol: start JSON → PCM16 → stop; the service returns ready / partial / final / done / error."
            : "流式协议：start JSON → PCM16 → stop；服务返回 ready / partial / final / done / error。";
        CloseButton.Content = english ? "Close" : "关闭";
        SaveAndTestButton.Content = _busy
            ? english ? "Testing…" : "正在验证…"
            : english ? "Save and verify" : "保存并验证";
        ApplyProviderPresentation();
        RefreshStatus();
    }

    private void ApplyProviderPresentation()
    {
        var provider = SelectedProvider();
        SystemPanel.Visibility = provider == MicroVoiceProviders.System
            ? Visibility.Visible
            : Visibility.Collapsed;
        LocalPanel.Visibility = provider == MicroVoiceProviders.LocalQwen
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemotePanel.Visibility = provider == MicroVoiceProviders.RemoteWebSocket
            ? Visibility.Visible
            : Visibility.Collapsed;
        CredentialPanel.Visibility = provider == MicroVoiceProviders.RemoteWebSocket
            ? Visibility.Visible
            : Visibility.Collapsed;
        ProviderDetailText.Text = provider switch
        {
            MicroVoiceProviders.System => _localization.IsEnglish
                ? "Recognition runs in the keypad through Windows Speech."
                : "识别由小键盘通过 Windows Speech 完成。",
            MicroVoiceProviders.LocalQwen => _localization.IsEnglish
                ? "The keypad owns startup, health checks, and the loopback dsh-stream-v1 connection."
                : "启动、健康检查和本机 dsh-stream-v1 连接都由小键盘负责。",
            _ => _localization.IsEnglish
                ? "The keypad sends audio directly to the configured secure service."
                : "小键盘直接把音频发送到已配置的安全服务。",
        };
        RefreshCredentialStatus();
    }

    private void RefreshStatus()
    {
        var settings = _profileSettings.Current.VoiceSettings;
        SetupStatusText.Text = settings.SetupCompleted
            ? _localization.IsEnglish
                ? "This keypad's voice provider is verified and ready."
                : "此小键盘的语音提供商已验证，可以使用。"
            : _localization.IsEnglish
                ? "Verify a provider here before using either microphone key or DeepSeek's voice button."
                : "请先在这里验证提供商，再使用麦克风键或 DeepSeek 的语音按钮。";
        SetupStatusBorder.Background = new SolidColorBrush(settings.SetupCompleted
            ? Color.FromRgb(0xEE, 0xF8, 0xF2)
            : Color.FromRgb(0xF0, 0xF3, 0xFF));
        SetupStatusBorder.BorderBrush = new SolidColorBrush(settings.SetupCompleted
            ? Color.FromRgb(0xC9, 0xE7, 0xD4)
            : Color.FromRgb(0xD7, 0xDF, 0xFF));
        SetupStatusText.Foreground = new SolidColorBrush(settings.SetupCompleted
            ? Color.FromRgb(0x3F, 0x7B, 0x59)
            : Color.FromRgb(0x50, 0x62, 0xB5));
    }

    private void RefreshCredentialStatus()
    {
        var provider = SelectedProvider();
        if (provider != MicroVoiceProviders.RemoteWebSocket)
        {
            CredentialStatusText.Text = string.Empty;
            ClearCredentialButton.IsEnabled = false;
            return;
        }

        try
        {
            var configured = _voiceInput.HasCredential(provider);
            CredentialStatusText.Text = configured
                ? _localization.IsEnglish
                    ? "Stored in Windows Credential Manager; leave blank to keep it."
                    : "已保存在 Windows 凭据管理器；留空会保留现有密钥。"
                : _localization.IsEnglish
                    ? "Optional unless the selected service requires authentication."
                    : "可选；仅在所选服务要求鉴权时填写。";
            ClearCredentialButton.IsEnabled = configured && !_busy;
        }
        catch (Exception exception)
        {
            CredentialStatusText.Text = exception.Message;
            ClearCredentialButton.IsEnabled = false;
        }
    }

    private MicroVoiceProfile ReadDraft()
    {
        var provider = SelectedProvider();
        var current = _profileSettings.Current.VoiceSettings;
        if (!int.TryParse(
                LocalReadyTimeoutTextBox.Text.Trim(),
                out var readyTimeout))
        {
            throw new InvalidOperationException(_localization.IsEnglish
                ? "The local Qwen ASR ready timeout must be a whole number."
                : "本地 Qwen ASR 就绪超时必须是整数。");
        }
        var value = current with
        {
            Provider = provider,
            Language = LanguageTextBox.Text.Trim(),
            AutoSubmit = AutoSubmitToggle.IsChecked == true,
            SetupCompleted = false,
            LocalStreamUrl = LocalUrlTextBox.Text.Trim(),
            LocalModel = LocalModelTextBox.Text.Trim(),
            LocalStartMode = SelectedLocalStartMode(),
            LocalHealthUrl = LocalHealthUrlTextBox.Text.Trim(),
            LocalLauncherPath = LocalLauncherTextBox.Text.Trim(),
            LocalWorkingDirectory =
                LocalWorkingDirectoryTextBox.Text.Trim(),
            LocalDistribution = LocalDistributionTextBox.Text.Trim(),
            LocalPythonPath = LocalPythonPathTextBox.Text.Trim(),
            LocalReadyTimeoutSeconds = readyTimeout,
            LocalStopWithKeypad = LocalStopWithKeypadToggle.IsChecked == true,
            RemoteUrl = RemoteUrlTextBox.Text.Trim(),
            RemoteModel = RemoteModelTextBox.Text.Trim(),
        };
        MicroVoiceInputService.Validate(value);
        return value;
    }

    private string SelectedProvider() =>
        ProviderCombo.SelectedItem is ProviderChoice choice
            ? choice.Id
            : MicroVoiceProviders.System;

    private string SelectedLocalStartMode() =>
        LocalStartModeCombo.SelectedItem is StartModeChoice choice
            ? choice.Id
            : MicroLocalVoiceStartModes.OnDemand;

    private void UseLocalExampleButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var example = MicroVoiceProfile.Default;
        LocalUrlTextBox.Text = example.LocalStreamUrl;
        LocalModelTextBox.Text = example.LocalModel;
        LocalHealthUrlTextBox.Text = example.LocalHealthUrl;
        LocalLauncherTextBox.Text = example.LocalLauncherPath;
        LocalWorkingDirectoryTextBox.Text = example.LocalWorkingDirectory;
        LocalDistributionTextBox.Text = example.LocalDistribution;
        LocalPythonPathTextBox.Text = example.LocalPythonPath;
        LocalReadyTimeoutTextBox.Text =
            example.LocalReadyTimeoutSeconds.ToString();
        LocalStopWithKeypadToggle.IsChecked = example.LocalStopWithKeypad;
        if (LocalStartModeCombo.ItemsSource is IEnumerable<StartModeChoice> modes)
        {
            LocalStartModeCombo.SelectedItem = modes.First(choice =>
                choice.Id == MicroLocalVoiceStartModes.KeypadStart);
        }
        FeedbackText.Foreground = new SolidColorBrush(
            Color.FromRgb(0x72, 0x77, 0x7C));
        FeedbackText.Text = _localization.IsEnglish
            ? "Loaded the portable local example with keypad auto-start. Save and verify to start it."
            : "已载入可移植的本机示例，并设为随小键盘启动；点击“保存并验证”即可启动。";
    }

    private void ProviderCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing)
        {
            ApplyProviderPresentation();
        }
    }

    private async void SaveAndTestButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            var value = ReadDraft();
            _busy = true;
            SetBusy(true);
            FeedbackText.Foreground = new SolidColorBrush(
                Color.FromRgb(0x72, 0x77, 0x7C));
            FeedbackText.Text = _localization.IsEnglish
                ? "Validating the keypad-owned voice provider…"
                : "正在验证由小键盘持有的语音提供商…";
            await _voiceInput.TestAndSaveAsync(
                value,
                CredentialPasswordBox.Password);
            CredentialPasswordBox.Password = string.Empty;
            FeedbackText.Foreground = new SolidColorBrush(
                Color.FromRgb(0x3F, 0x7B, 0x59));
            FeedbackText.Text = _localization.IsEnglish
                ? "Saved and verified on this keypad."
                : "已在此小键盘保存并验证。";
        }
        catch (Exception exception)
        {
            FeedbackText.Foreground = new SolidColorBrush(
                Color.FromRgb(0xB7, 0x55, 0x4F));
            FeedbackText.Text = exception.Message;
        }
        finally
        {
            _busy = false;
            SetBusy(false);
            RefreshStatus();
            RefreshCredentialStatus();
        }
    }

    private void ClearCredentialButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            _voiceInput.ClearCredential(SelectedProvider());
            CredentialPasswordBox.Password = string.Empty;
            FeedbackText.Text = _localization.IsEnglish
                ? "The keypad voice credential was removed."
                : "已删除此小键盘的语音凭据。";
            RefreshCredentialStatus();
        }
        catch (Exception exception)
        {
            FeedbackText.Text = exception.Message;
        }
    }

    private void SetBusy(bool value)
    {
        ProviderCombo.IsEnabled = !value;
        LanguageTextBox.IsEnabled = !value;
        AutoSubmitToggle.IsEnabled = !value;
        LocalUrlTextBox.IsEnabled = !value;
        LocalModelTextBox.IsEnabled = !value;
        UseLocalExampleButton.IsEnabled = !value;
        LocalStartModeCombo.IsEnabled = !value;
        LocalHealthUrlTextBox.IsEnabled = !value;
        LocalLauncherTextBox.IsEnabled = !value;
        LocalWorkingDirectoryTextBox.IsEnabled = !value;
        LocalDistributionTextBox.IsEnabled = !value;
        LocalPythonPathTextBox.IsEnabled = !value;
        LocalReadyTimeoutTextBox.IsEnabled = !value;
        LocalStopWithKeypadToggle.IsEnabled = !value;
        RemoteUrlTextBox.IsEnabled = !value;
        RemoteModelTextBox.IsEnabled = !value;
        CredentialPasswordBox.IsEnabled = !value;
        SaveAndTestButton.IsEnabled = !value;
        SaveAndTestButton.Content = value
            ? _localization.IsEnglish ? "Testing…" : "正在验证…"
            : _localization.IsEnglish ? "Save and verify" : "保存并验证";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_busy)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Localization_LanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.Invoke(RefreshPresentation);

    private void Window_Closed(object? sender, EventArgs e)
    {
        _localization.LanguageChanged -= Localization_LanguageChanged;
        Closed -= Window_Closed;
    }
}
