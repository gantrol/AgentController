using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection(WpfUiCollection.Name)]
public sealed class SettingsWindowDesignTests
{
    [Fact]
    public void VoiceSettingsExposeKeypadOwnedPortableQwenStartup()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            MicroVoiceInputService? voice = null;
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var profile = MicroProfileSettings.CreateTransient();
                profile.SetVoiceSettings(MicroVoiceProfile.Default with
                {
                    Provider = MicroVoiceProviders.LocalQwen,
                });
                var localization = new MicroLocalization(MicroLanguage.ZhCn);
                voice = new MicroVoiceInputService(profile);
                var window = new MicroVoiceSettingsWindow(
                    localization,
                    profile,
                    voice);

                window.Measure(new Size(760, 840));
                window.Arrange(new Rect(0, 0, 760, 840));
                var root = Assert.IsAssignableFrom<FrameworkElement>(
                    window.Content);
                root.Measure(new Size(760, 840));
                root.Arrange(new Rect(0, 0, 760, 840));
                window.UpdateLayout();

                Assert.Equal(3, window.ProviderCombo.Items.Count);
                Assert.Equal(3, window.LocalStartModeCombo.Items.Count);
                Assert.Equal(
                    "首次使用时启动",
                    window.LocalStartModeCombo.SelectedItem?.ToString());
                Assert.Equal(
                    "{AppDir}\\voice\\start-qwen3-asr-stream.ps1",
                    window.LocalLauncherTextBox.Text);
                Assert.Equal(
                    "{AppDir}\\voice",
                    window.LocalWorkingDirectoryTextBox.Text);
                Assert.Equal("Ubuntu", window.LocalDistributionTextBox.Text);
                Assert.Equal("600", window.LocalReadyTimeoutTextBox.Text);
                Assert.True(window.LocalStopWithKeypadToggle.IsChecked);
                Assert.Equal(Visibility.Visible, window.LocalPanel.Visibility);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.CredentialPanel.Visibility);
                Assert.Contains("小键盘", window.IntroText.Text);
                Assert.Contains("{AppDir}", window.LocalPathHintText.Text);

                localization.SetLanguage(MicroLanguage.EnUs);
                Assert.Equal("Voice input", window.HeadingText.Text);
                Assert.Equal(
                    "Start on first use",
                    window.LocalStartModeCombo.SelectedItem?.ToString());
                Assert.Contains("keypad", window.LocalDetailText.Text);

                window.Close();
            }
            catch (Exception exception)
            {
                error = exception;
            }
            finally
            {
                voice?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void SettingsPageUsesLayoutAndOptionsStructure()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var profile = MicroProfileSettings.CreateTransient();
                var localization = new MicroLocalization(MicroLanguage.ZhCn);
                var settingsRoot = Path.Combine(
                    Path.GetTempPath(),
                    "codex-micro-settings-window-tests",
                    Guid.NewGuid().ToString("N"));
                var configPath = Path.Combine(settingsRoot, "config.toml");
                var observer = new CodexMicroLayoutObserver(configPath);
                var harnessRegistry = new MicroHarnessRegistry(
                    settingsPath: Path.Combine(settingsRoot, "harness-settings.json"));
                var surface = new MicroSurfaceWindow(
                    localization,
                    profileSettings: profile);
                surface.DesignSurface.Measure(new Size(590, 610));
                surface.DesignSurface.Arrange(new Rect(0, 0, 590, 610));
                surface.DesignSurface.UpdateLayout();
                var window = new MicroSettingsWindow(
                    localization,
                    profile,
                    surface.DesignSurface,
                    observer,
                    new CodexMicroConfigWriter(configPath),
                    harnessRegistry,
                    isConnected: () => true);

                window.Measure(new Size(920, 760));
                window.Arrange(new Rect(0, 0, 920, 760));
                var root = Assert.IsAssignableFrom<FrameworkElement>(
                    window.Content);
                root.Measure(new Size(920, 760));
                root.Arrange(new Rect(0, 0, 920, 760));
                window.UpdateLayout();

                Assert.Equal(920, window.Width, 3);
                Assert.Equal(760, window.Height, 3);
                Assert.False(window.ShowInTaskbar);
                Assert.True(window.AllowsTransparency);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                Assert.Equal("Layout", window.LayoutHeadingText.Text);
                Assert.Equal("Options", window.OptionsHeadingText.Text);
                Assert.Equal(4, window.AgentSourceCombo.Items.Count);
                Assert.Equal(4, window.KnobModeCombo.Items.Count);
                Assert.Equal(3, window.MicrophoneModeCombo.Items.Count);
                Assert.Equal(
                    Visibility.Visible,
                    window.InvertDialDirectionOptionRow.Visibility);
                Assert.False(window.InvertDialDirectionToggle.IsChecked);
                Assert.Equal(3, window.QuickModelACombo.Items.Count);
                Assert.Equal("Sol", window.QuickModelACombo.SelectedItem?.ToString());
                Assert.Equal("Luna", window.QuickModelBCombo.SelectedItem?.ToString());
                Assert.Contains(
                    window.HarnessCombo.Items.Cast<MicroHarnessDefinition>(),
                    item => item.Id == "codex");
                Assert.Contains(
                    window.HarnessCombo.Items.Cast<MicroHarnessDefinition>(),
                    item => item.Id == "deepseek-harness");
                Assert.Same(
                    surface.DesignSurface,
                    window.LiveMicroPreviewBrush.Visual);
                Assert.Equal(342, window.LiveMicroPreview.Width, 3);
                Assert.Equal(353.5, window.LiveMicroPreview.Height, 3);
                Assert.Equal(375, window.LayoutCard.Height, 3);
                Assert.Equal(
                    Visibility.Visible,
                    window.EditCombinedMicrophoneButton.Visibility);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.EditMicrophone1Button.Visibility);
                Assert.Contains("已连接", window.ConnectionStatusText.Text);

                window.KnobModeCombo.SelectedIndex = 1;
                Assert.Equal("reasoning", observer.Current.EncoderMode);
                Assert.Contains("encoderMode = \"reasoning\"", File.ReadAllText(configPath));

                profile.SetActiveHarness("deepseek-harness");
                Assert.Equal(
                    Visibility.Collapsed,
                    window.InvertDialDirectionOptionRow.Visibility);
                Assert.False(window.InvertDialDirectionToggle.IsEnabled);
                Assert.Equal(Visibility.Visible, window.HarnessManagementCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.HarnessAdapterCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.HarnessKeyMapCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.QuickModelARow.Visibility);
                Assert.Equal(Visibility.Collapsed, window.ReconnectButton.Visibility);
                Assert.True(window.LayoutCard.IsEnabled);
                Assert.False(window.AgentSourceCombo.IsEnabled);
                Assert.Single(window.AgentSourceCombo.Items);
                Assert.Equal(3, window.KnobModeCombo.Items.Count);
                Assert.True(window.KnobModeCombo.IsEnabled);
                Assert.Equal(2, window.MicrophoneModeCombo.Items.Count);
                Assert.True(window.SingleTapToggle.IsEnabled);
                Assert.Equal(16, window.HarnessAction06Combo.Items.Count);
                Assert.Equal("新建会话", window.HarnessAction06Combo.SelectedItem?.ToString());
                Assert.Equal("Fork 当前会话", window.HarnessAction09Combo.SelectedItem?.ToString());
                Assert.Equal("对话 / 轨迹", window.HarnessAction07Combo.SelectedItem?.ToString());
                Assert.Equal("停止当前生成", window.HarnessAction08Combo.SelectedItem?.ToString());
                Assert.Equal(Visibility.Visible, window.JoystickOptionRow.Visibility);
                Assert.Equal("方向 ›", window.JoystickMappingButton.Content);
                Assert.Equal(
                    "上一个会话",
                    window.HarnessJoystickUpCombo.SelectedItem?.ToString());
                Assert.Equal(
                    "下一个会话",
                    window.HarnessJoystickDownCombo.SelectedItem?.ToString());
                Assert.Equal(
                    "切换侧边栏",
                    window.HarnessJoystickLeftCombo.SelectedItem?.ToString());
                Assert.Equal(
                    "打开详情栏",
                    window.HarnessJoystickRightCombo.SelectedItem?.ToString());

                window.HarnessJoystickUpCombo.SelectedItem =
                    window.HarnessJoystickUpCombo.Items
                        .Cast<object>()
                        .First(item => item.ToString() == "关闭详情栏");
                Assert.Equal(
                    MicroHarnessActionIds.CloseDetails,
                    harnessRegistry.ResolveKeyMap("deepseek-harness")
                        .Resolve(MicroHarnessControlIds.JoystickUp));
                var restoredHarnessRegistry = new MicroHarnessRegistry(
                    settingsPath: Path.Combine(
                        settingsRoot,
                        "harness-settings.json"));
                Assert.Equal(
                    MicroHarnessActionIds.CloseDetails,
                    restoredHarnessRegistry.ResolveKeyMap("deepseek-harness")
                        .Resolve(MicroHarnessControlIds.JoystickUp));

                window.ResetButton.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(
                    MicroHarnessActionIds.PreviousSession,
                    harnessRegistry.ResolveKeyMap("deepseek-harness")
                        .Resolve(MicroHarnessControlIds.JoystickUp));
                Assert.Equal(
                    MicroHarnessActionIds.NextSession,
                    harnessRegistry.ResolveKeyMap("deepseek-harness")
                        .Resolve(MicroHarnessControlIds.JoystickDown));
                Assert.Equal(
                    MicroHarnessActionIds.ToggleSidebar,
                    harnessRegistry.ResolveKeyMap("deepseek-harness")
                        .Resolve(MicroHarnessControlIds.JoystickLeft));
                Assert.Equal(
                    MicroHarnessActionIds.OpenDetails,
                    harnessRegistry.ResolveKeyMap("deepseek-harness")
                        .Resolve(MicroHarnessControlIds.JoystickRight));
                window.KnobModeCombo.SelectedIndex = 2;
                Assert.Equal(
                    MicroHarnessKnobModes.RecentSessions,
                    harnessRegistry.ResolveKnobMode("deepseek-harness"));
                window.SingleTapToggle.IsChecked = false;
                Assert.False(profile.Current.SingleTapAgentKeys);
                window.SingleTapToggle.IsChecked = true;
                Assert.True(profile.Current.SingleTapAgentKeys);
                window.MicrophoneModeCombo.SelectedIndex = 1;
                Assert.True(profile.Current.TapToToggleVoice);
                Assert.Equal(string.Empty, window.HarnessExecutableTextBox.Text);
                Assert.Equal(
                    "http://127.0.0.1:3080/__agentcontroller/micro/request",
                    window.HarnessControlUriTextBox.Text);

                var harnessPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_HARNESS_SETTINGS_PREVIEW");
                if (!string.IsNullOrWhiteSpace(harnessPreviewPath))
                {
                    window.KnobModeCombo.SelectedIndex = 0;
                    window.SettingsScrollViewer.ScrollToTop();
                    window.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(
                        920,
                        760,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(harnessPreviewPath)!);
                    using var stream = File.Create(harnessPreviewPath);
                    encoder.Save(stream);
                }

                profile.SetActiveHarness("codex");
                Assert.Equal(
                    Visibility.Visible,
                    window.InvertDialDirectionOptionRow.Visibility);
                Assert.Equal(Visibility.Collapsed, window.HarnessAdapterCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.HarnessManagementCard.Visibility);
                Assert.Equal(Visibility.Collapsed, window.HarnessKeyMapCard.Visibility);
                Assert.Equal(Visibility.Visible, window.QuickModelARow.Visibility);
                Assert.Equal(Visibility.Visible, window.ReconnectButton.Visibility);
                Assert.True(window.LayoutCard.IsEnabled);
                Assert.True(window.AgentSourceCombo.IsEnabled);
                Assert.Equal(4, window.AgentSourceCombo.Items.Count);
                Assert.Equal(4, window.KnobModeCombo.Items.Count);
                Assert.Equal("reasoning", observer.Current.EncoderMode);

                window.InvertDialDirectionToggle.IsChecked = true;
                Assert.True(profile.Current.InvertDialDirection);

                window.QuickModelBCombo.SelectedIndex = 2;
                Assert.Equal(
                    CodexQuickModel.Terra,
                    profile.Current.QuickModelB);
                Assert.Equal("Terra", window.QuickModelBCombo.SelectedItem?.ToString());
                localization.SetLanguage(MicroLanguage.EnUs);
                Assert.Equal("Micro software settings", window.WindowTitleText.Text);
                Assert.Contains("connected", window.ConnectionStatusText.Text);

                profile.Reset();
                localization.SetLanguage(MicroLanguage.ZhCn);
                window.SettingsScrollViewer.ScrollToTop();
                window.UpdateLayout();

                var previewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_SETTINGS_PREVIEW");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    var bitmap = new RenderTargetBitmap(
                        920,
                        760,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
                    using var stream = File.Create(previewPath);
                    encoder.Save(stream);
                }

                window.Close();
                Assert.Null(window.LiveMicroPreviewBrush.Visual);
                surface.CloseForApplicationExit();
                observer.Dispose();
                Directory.Delete(settingsRoot, recursive: true);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void KeycapEditorUsesSearchableSixColumnCatalogAndActionPicker()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var configPath = Path.Combine(
                    Path.GetTempPath(),
                    "codex-micro-editor-tests",
                    Guid.NewGuid().ToString("N"),
                    "config.toml");
                using var observer = new CodexMicroLayoutObserver(configPath);
                var editor = new KeycapEditorWindow(
                    "ACT07",
                    observer.Current.GetSlot("ACT07"),
                    new MicroLocalization(MicroLanguage.ZhCn),
                    new CodexMicroConfigWriter(configPath),
                    observer);

                editor.Measure(new Size(940, 820));
                editor.Arrange(new Rect(0, 0, 940, 820));
                var root = Assert.IsAssignableFrom<FrameworkElement>(
                    editor.Content);
                root.Measure(new Size(940, 820));
                root.Arrange(new Rect(0, 0, 940, 820));
                editor.UpdateLayout();

                Assert.Equal("编辑键帽", editor.EditorTitleText.Text);
                Assert.Contains("ACT07", editor.EditorSubtitleText.Text);
                Assert.True(editor.KeycapList.Items.Count > 30);
                Assert.Equal(
                    "APPR",
                    ((CodexKeycapDefinition)editor.KeycapList.SelectedItem).Id);
                Assert.True(typeof(CodexKeycapDefinition)
                    .GetProperty(nameof(CodexKeycapDefinition.IconId))!
                    .GetMethod!.IsPublic);
                Assert.Equal(
                    "FAST",
                    ((CodexKeycapDefinition)editor.KeycapList.Items[0]).IconId);
                Assert.True(editor.ActionCombo.Items.Count > 10);
                Assert.Equal(940, editor.Width, 3);
                Assert.Equal(820, editor.Height, 3);

                editor.SearchBox.Text = "LAB";
                var filteredKeycap = Assert.Single(
                    editor.KeycapList.Items.Cast<CodexKeycapDefinition>());
                Assert.Equal("LAB", filteredKeycap.Id);

                var previewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_KEYCAP_EDITOR_PREVIEW");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    editor.SearchBox.Text = string.Empty;
                    editor.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(
                        940,
                        820,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
                    using var stream = File.Create(previewPath);
                    encoder.Save(stream);
                }

                editor.Close();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void HarnessKeycapEditorUsesTheSameSubpageAndNativeActions()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                var root = Path.Combine(
                    Path.GetTempPath(),
                    "codex-micro-harness-editor-tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                var localization = new MicroLocalization(MicroLanguage.ZhCn);
                var registry = new MicroHarnessRegistry(
                    settingsPath: Path.Combine(root, "harness-settings.json"));
                var editor = new KeycapEditorWindow(
                    MicroHarnessControlIds.Action07,
                    "deepseek-harness",
                    localization,
                    registry);

                editor.Measure(new Size(940, 820));
                editor.Arrange(new Rect(0, 0, 940, 820));
                var editorRoot = Assert.IsAssignableFrom<FrameworkElement>(
                    editor.Content);
                editorRoot.Measure(new Size(940, 820));
                editorRoot.Arrange(new Rect(0, 0, 940, 820));
                editor.UpdateLayout();

                Assert.Contains("Harness 原生动作", editor.EditorSubtitleText.Text);
                Assert.Equal("对话 / 轨迹", editor.ActionCombo.SelectedItem?.ToString());
                Assert.True(editor.KeycapList.Items.Count >= 13);
                Assert.Contains(
                    editor.KeycapList.Items.Cast<object>(),
                    item => item.ToString()?.Contains("GOAL", StringComparison.Ordinal) == true);
                var goal = editor.KeycapList.Items.Cast<object>()
                    .First(item => item.ToString()?.Contains(
                        "GOAL",
                        StringComparison.Ordinal) == true);
                var goalIcon = goal.GetType().GetProperty("IconId");
                Assert.NotNull(goalIcon);
                Assert.True(goalIcon.GetMethod!.IsPublic);
                Assert.Equal("GOAL", goalIcon.GetValue(goal));

                var previewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_HARNESS_KEYCAP_EDITOR_PREVIEW");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    var bitmap = new RenderTargetBitmap(
                        940,
                        820,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    bitmap.Render(editorRoot);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
                    using var stream = File.Create(previewPath);
                    encoder.Save(stream);
                }

                editor.SearchBox.Text = "轨迹";
                Assert.Single(editor.KeycapList.Items);

                localization.SetLanguage(MicroLanguage.EnUs);
                Assert.Equal(
                    "Conversation / trajectory",
                    editor.ActionCombo.SelectedItem?.ToString());
                editor.Close();
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }
}
