using System.Drawing;
using System.Windows.Forms;
using AgentController.MicroSurface.Wpf;
using CodexMicro.Desktop.Services;

namespace CodexMicro.DesktopHost;

internal sealed class MicroTrayIcon : IDisposable
{
    private readonly MicroSurfaceController _surface;
    private readonly MicroLocalization _localization;
    private readonly MicroStartupRegistration _startupRegistration;
    private readonly Action<MicroLanguage> _setLanguage;
    private readonly Action _exit;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _languageItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _autoLanguageItem;
    private readonly ToolStripMenuItem _zhCnLanguageItem;
    private readonly ToolStripMenuItem _enUsLanguageItem;
    private readonly ToolStripMenuItem _exitItem;
    private Icon? _icon;
    private bool _disposed;

    internal MicroTrayIcon(
        MicroSurfaceController surface,
        MicroLocalization localization,
        MicroStartupRegistration startupRegistration,
        Action<MicroLanguage> setLanguage,
        Action exit)
    {
        _surface = surface;
        _localization = localization;
        _startupRegistration = startupRegistration;
        _setLanguage = setLanguage;
        _exit = exit;
        _menu = new ContextMenuStrip();
        _toggleItem = new ToolStripMenuItem(
            string.Empty,
            image: null,
            (_, _) => Toggle());
        _languageItem = new ToolStripMenuItem();
        _startupItem = new ToolStripMenuItem(
            string.Empty,
            image: null,
            (_, _) => ToggleStartup());
        _autoLanguageItem = CreateLanguageItem(MicroLanguage.Auto);
        _zhCnLanguageItem = CreateLanguageItem(MicroLanguage.ZhCn);
        _enUsLanguageItem = CreateLanguageItem(MicroLanguage.EnUs);
        _languageItem.DropDownItems.AddRange(
        [
            _autoLanguageItem,
            _zhCnLanguageItem,
            _enUsLanguageItem,
        ]);
        _exitItem = new ToolStripMenuItem(
            string.Empty,
            image: null,
            (_, _) => _exit());
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(_languageItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exitItem);
        _menu.Opening += (_, _) =>
        {
            _localization.RefreshAutoLanguage();
            RefreshText();
        };

        _icon = LoadApplicationIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "Codex Micro",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => Toggle();
        _localization.LanguageChanged += Localization_LanguageChanged;
        RefreshText();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= Localization_LanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
        _icon = null;
    }

    private void Toggle()
    {
        if (_surface.IsVisible)
        {
            _surface.Hide();
        }
        else
        {
            _surface.Show();
        }

        RefreshText();
    }

    private ToolStripMenuItem CreateLanguageItem(MicroLanguage language)
    {
        return new ToolStripMenuItem(
            string.Empty,
            image: null,
            (_, _) => _setLanguage(language));
    }

    private void Localization_LanguageChanged(object? sender, EventArgs e) =>
        RefreshText();

    private void ToggleStartup()
    {
        try
        {
            _startupRegistration.SetEnabled(!_startupRegistration.IsEnabled);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            _notifyIcon.ShowBalloonTip(
                4000,
                _localization.IsEnglish
                    ? "Codex Micro startup"
                    : "Codex Micro 开机自启动",
                _localization.IsEnglish
                    ? $"Could not update startup: {exception.Message}"
                    : $"无法更新开机自启动：{exception.Message}",
                ToolTipIcon.Error);
        }

        RefreshText();
    }

    private void RefreshText()
    {
        var english = _localization.IsEnglish;
        _toggleItem.Text = _surface.IsVisible
            ? english ? "Hide keypad" : "收起小键盘"
            : english ? "Show keypad" : "显示小键盘";
        _languageItem.Text = english ? "Language" : "语言";
        _startupItem.Text = english
            ? "Start with Windows"
            : "开机自启动";
        _startupItem.Checked = _startupRegistration.IsEnabled;
        _autoLanguageItem.Text = english
            ? "Auto (Agent Controller / Windows)"
            : "自动（跟随 Agent Controller / Windows）";
        _zhCnLanguageItem.Text = "简体中文";
        _enUsLanguageItem.Text = "English";
        _exitItem.Text = english ? "Exit" : "退出";
        _notifyIcon.Text = english
            ? "Codex Micro keypad"
            : "Codex Micro 小键盘";
        _autoLanguageItem.Checked =
            _localization.SelectedLanguage == MicroLanguage.Auto;
        _zhCnLanguageItem.Checked =
            _localization.SelectedLanguage == MicroLanguage.ZhCn;
        _enUsLanguageItem.Checked =
            _localization.SelectedLanguage == MicroLanguage.EnUs;
    }

    private static Icon LoadApplicationIcon()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            using var extracted = Icon.ExtractAssociatedIcon(executable);
            if (extracted is not null)
            {
                return (Icon)extracted.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
