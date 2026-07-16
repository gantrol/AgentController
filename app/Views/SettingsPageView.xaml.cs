using System.Windows;
using CodexController.Localization;

namespace CodexController.Views;

public partial class SettingsPageView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StringsProperty =
        DependencyProperty.Register(
            nameof(Strings),
            typeof(LocalizedStrings),
            typeof(SettingsPageView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LocalizationProperty =
        DependencyProperty.Register(
            nameof(Localization),
            typeof(LocalizationService),
            typeof(SettingsPageView),
            new PropertyMetadata(null));

    public SettingsPageView()
    {
        InitializeComponent();
    }

    public LocalizedStrings? Strings
    {
        get => (LocalizedStrings?)GetValue(StringsProperty);
        set => SetValue(StringsProperty, value);
    }

    public LocalizationService? Localization
    {
        get => (LocalizationService?)GetValue(LocalizationProperty);
        set => SetValue(LocalizationProperty, value);
    }
}
