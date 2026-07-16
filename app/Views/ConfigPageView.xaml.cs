using System.Windows;
using CodexController.Localization;

namespace CodexController.Views;

public partial class ConfigPageView : System.Windows.Controls.UserControl
{
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

    public LocalizedStrings? Strings
    {
        get => (LocalizedStrings?)GetValue(StringsProperty);
        set => SetValue(StringsProperty, value);
    }
}
