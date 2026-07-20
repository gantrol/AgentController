using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace CodexController.Views;

[Flags]
public enum ControllerDPadDirection
{
    None = 0,
    Up = 1,
    Right = 2,
    Down = 4,
    Left = 8,
}

public partial class ControllerGlyphView : UserControl
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(ControllerGlyphView),
            new PropertyMetadata(string.Empty, OnGlyphChanged));

    public static readonly DependencyProperty DPadDirectionProperty =
        DependencyProperty.Register(
            nameof(DPadDirection),
            typeof(ControllerDPadDirection),
            typeof(ControllerGlyphView),
            new PropertyMetadata(ControllerDPadDirection.None));

    public static readonly DependencyProperty IsDPadGlyphProperty =
        DependencyProperty.Register(
            nameof(IsDPadGlyph),
            typeof(bool),
            typeof(ControllerGlyphView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDPadUpHighlightedProperty =
        DependencyProperty.Register(
            nameof(IsDPadUpHighlighted),
            typeof(bool),
            typeof(ControllerGlyphView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDPadRightHighlightedProperty =
        DependencyProperty.Register(
            nameof(IsDPadRightHighlighted),
            typeof(bool),
            typeof(ControllerGlyphView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDPadDownHighlightedProperty =
        DependencyProperty.Register(
            nameof(IsDPadDownHighlighted),
            typeof(bool),
            typeof(ControllerGlyphView),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsDPadLeftHighlightedProperty =
        DependencyProperty.Register(
            nameof(IsDPadLeftHighlighted),
            typeof(bool),
            typeof(ControllerGlyphView),
            new PropertyMetadata(false));

    public ControllerGlyphView()
    {
        InitializeComponent();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public ControllerDPadDirection DPadDirection
    {
        get => (ControllerDPadDirection)GetValue(DPadDirectionProperty);
        private set => SetValue(DPadDirectionProperty, value);
    }

    public bool IsDPadGlyph
    {
        get => (bool)GetValue(IsDPadGlyphProperty);
        private set => SetValue(IsDPadGlyphProperty, value);
    }

    public bool IsDPadUpHighlighted
    {
        get => (bool)GetValue(IsDPadUpHighlightedProperty);
        private set => SetValue(IsDPadUpHighlightedProperty, value);
    }

    public bool IsDPadRightHighlighted
    {
        get => (bool)GetValue(IsDPadRightHighlightedProperty);
        private set => SetValue(IsDPadRightHighlightedProperty, value);
    }

    public bool IsDPadDownHighlighted
    {
        get => (bool)GetValue(IsDPadDownHighlightedProperty);
        private set => SetValue(IsDPadDownHighlightedProperty, value);
    }

    public bool IsDPadLeftHighlighted
    {
        get => (bool)GetValue(IsDPadLeftHighlightedProperty);
        private set => SetValue(IsDPadLeftHighlightedProperty, value);
    }

    private static void OnGlyphChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not ControllerGlyphView view)
        {
            return;
        }

        var glyph = (eventArgs.NewValue as string)?.Trim().ToUpperInvariant()
            ?? string.Empty;
        var direction = glyph switch
        {
            "↑" or "UP" => ControllerDPadDirection.Up,
            "→" or "RIGHT" => ControllerDPadDirection.Right,
            "↓" or "DOWN" => ControllerDPadDirection.Down,
            "←" or "LEFT" => ControllerDPadDirection.Left,
            _ => ResolveCombinedDPadDirection(glyph),
        };
        view.DPadDirection = direction;
        view.IsDPadGlyph = direction != ControllerDPadDirection.None;
        view.IsDPadUpHighlighted = direction.HasFlag(
            ControllerDPadDirection.Up);
        view.IsDPadRightHighlighted = direction.HasFlag(
            ControllerDPadDirection.Right);
        view.IsDPadDownHighlighted = direction.HasFlag(
            ControllerDPadDirection.Down);
        view.IsDPadLeftHighlighted = direction.HasFlag(
            ControllerDPadDirection.Left);
    }

    private static ControllerDPadDirection ResolveCombinedDPadDirection(
        string glyph)
    {
        var direction = ControllerDPadDirection.None;
        if (glyph.Contains('↑'))
        {
            direction |= ControllerDPadDirection.Up;
        }

        if (glyph.Contains('→'))
        {
            direction |= ControllerDPadDirection.Right;
        }

        if (glyph.Contains('↓'))
        {
            direction |= ControllerDPadDirection.Down;
        }

        if (glyph.Contains('←'))
        {
            direction |= ControllerDPadDirection.Left;
        }

        return direction;
    }
}
