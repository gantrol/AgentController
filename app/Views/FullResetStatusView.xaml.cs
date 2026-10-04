using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;
using FontFamily = System.Windows.Media.FontFamily;
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
        RenderText();
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

    private void ShowDetailsOnKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (IsKeyboardFocused && !string.IsNullOrWhiteSpace(ToolTipText))
            HelpTip.Toggle(this);
    }

    private static void OnTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is FullResetStatusView view)
        {
            view.SetValue(
                HasCreditsPropertyKey,
                !string.IsNullOrWhiteSpace(eventArgs.NewValue as string));
            view.RenderText();
        }
    }

    private void RenderText()
    {
        if (StatusText is null) return;
        StatusText.Inlines.Clear();
        foreach (var part in Regex.Split(Text ?? string.Empty, @"([0-9]+(?:[-:][0-9]+)*)"))
        {
            var run = new Run(part);
            if (part.Length > 0 && char.IsAsciiDigit(part[0]))
                run.FontFamily = (FontFamily)FindResource("Font.Mono");
            StatusText.Inlines.Add(run);
        }
    }
}
