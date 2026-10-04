using System.Windows;
using System.Windows.Media;
using CodexController.Localization;
using CodexController.ViewModels;

namespace CodexController.Views;

public partial class ConfigPageView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty AgentNameProperty =
        DependencyProperty.Register(
            nameof(AgentName),
            typeof(string),
            typeof(ConfigPageView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AgentLogoProperty =
        DependencyProperty.Register(
            nameof(AgentLogo),
            typeof(ImageSource),
            typeof(ConfigPageView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty AgentSettingsProperty =
        DependencyProperty.Register(
            nameof(AgentSettings),
            typeof(SettingsPageViewModel),
            typeof(ConfigPageView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty FullResetTextProperty =
        DependencyProperty.Register(
            nameof(FullResetText),
            typeof(string),
            typeof(ConfigPageView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty FullResetToolTipProperty =
        DependencyProperty.Register(
            nameof(FullResetToolTip),
            typeof(string),
            typeof(ConfigPageView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StringsProperty =
        DependencyProperty.Register(
            nameof(Strings),
            typeof(LocalizedStrings),
            typeof(ConfigPageView),
            new PropertyMetadata(null));

    public ConfigPageView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? AgentTargetRequested;

    private void AgentTargetButton_Click(object sender, RoutedEventArgs e)
    {
        AgentTargetRequested?.Invoke(this, e);
    }

    private void ConfigHelp_Click(object sender, RoutedEventArgs e)
    {
        HelpTip.Toggle(ConfigHelpButton);
    }

    public LocalizedStrings? Strings
    {
        get => (LocalizedStrings?)GetValue(StringsProperty);
        set => SetValue(StringsProperty, value);
    }

    public string AgentName
    {
        get => (string)GetValue(AgentNameProperty);
        set => SetValue(AgentNameProperty, value);
    }

    public ImageSource? AgentLogo
    {
        get => (ImageSource?)GetValue(AgentLogoProperty);
        set => SetValue(AgentLogoProperty, value);
    }

    public SettingsPageViewModel? AgentSettings
    {
        get => (SettingsPageViewModel?)GetValue(AgentSettingsProperty);
        set => SetValue(AgentSettingsProperty, value);
    }

    public string FullResetText
    {
        get => (string)GetValue(FullResetTextProperty);
        set => SetValue(FullResetTextProperty, value);
    }

    public string FullResetToolTip
    {
        get => (string)GetValue(FullResetToolTipProperty);
        set => SetValue(FullResetToolTipProperty, value);
    }
}
