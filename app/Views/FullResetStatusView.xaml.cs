using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace CodexController.Views;

public partial class FullResetStatusView : UserControl
{
    private static readonly DependencyPropertyKey HasCreditsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasCredits),
            typeof(bool),
            typeof(FullResetStatusView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasCreditsProperty =
        HasCreditsPropertyKey.DependencyProperty;

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(FullResetStatusView),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty ToolTipTextProperty =
        DependencyProperty.Register(
            nameof(ToolTipText),
            typeof(string),
            typeof(FullResetStatusView),
            new PropertyMetadata(string.Empty));

    public FullResetStatusView()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string ToolTipText
    {
        get => (string)GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    public bool HasCredits => (bool)GetValue(HasCreditsProperty);

    private static void OnTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is FullResetStatusView view)
        {
            view.SetValue(
                HasCreditsPropertyKey,
                !string.IsNullOrWhiteSpace(eventArgs.NewValue as string));
        }
    }
}
