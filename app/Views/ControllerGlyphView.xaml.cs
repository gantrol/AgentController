using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace CodexController.Views;

public partial class ControllerGlyphView : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(ControllerGlyphView),
            new PropertyMetadata(string.Empty));

    public ControllerGlyphView()
    {
        InitializeComponent();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
