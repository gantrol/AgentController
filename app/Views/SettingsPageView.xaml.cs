using System.Windows;
using CodexController.Localization;

namespace CodexController.Views;

public partial class SettingsPageView : System.Windows.Controls.UserControl
{
    private ComponentGalleryWindow? _componentGallery;

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement button) HelpTip.Toggle(button);
    }

    private void ComponentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is not App { IsDevelopmentMode: true })
            return;

        if (_componentGallery is not null)
        {
            if (_componentGallery.WindowState == WindowState.Minimized)
                _componentGallery.WindowState = WindowState.Normal;
            _componentGallery.Activate();
            return;
        }

        _componentGallery = new ComponentGalleryWindow { Owner = Window.GetWindow(this) };
        _componentGallery.Closed += (_, _) => _componentGallery = null;
        _componentGallery.Show();
    }

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
        ComponentsButton.Visibility = System.Windows.Application.Current is App { IsDevelopmentMode: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
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
