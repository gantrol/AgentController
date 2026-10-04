using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace CodexController.Views;

public static class ContextHint
{
    public static readonly DependencyProperty IsScopeProperty = DependencyProperty.RegisterAttached(
        "IsScope", typeof(bool), typeof(ContextHint), new PropertyMetadata(false, ScopeChanged));
    public static readonly DependencyProperty DefaultTextProperty = DependencyProperty.RegisterAttached(
        "DefaultText", typeof(string), typeof(ContextHint), new PropertyMetadata(string.Empty, DefaultTextChanged));
    public static readonly DependencyProperty IsDisplayProperty = DependencyProperty.RegisterAttached(
        "IsDisplay", typeof(bool), typeof(ContextHint), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey TitlePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Title", typeof(string), typeof(ContextHint),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.Inherits));
    private static readonly DependencyPropertyKey DescriptionPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Description", typeof(string), typeof(ContextHint),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty TitleProperty = TitlePropertyKey.DependencyProperty;
    public static readonly DependencyProperty DescriptionProperty = DescriptionPropertyKey.DependencyProperty;

    public static bool GetIsScope(DependencyObject element) => (bool)element.GetValue(IsScopeProperty);
    public static void SetIsScope(DependencyObject element, bool value) => element.SetValue(IsScopeProperty, value);
    public static string GetDefaultText(DependencyObject element) => (string)element.GetValue(DefaultTextProperty);
    public static void SetDefaultText(DependencyObject element, string value) => element.SetValue(DefaultTextProperty, value);
    public static bool GetIsDisplay(DependencyObject element) => (bool)element.GetValue(IsDisplayProperty);
    public static void SetIsDisplay(DependencyObject element, bool value) => element.SetValue(IsDisplayProperty, value);
    public static string GetTitle(DependencyObject element) => (string)element.GetValue(TitleProperty);
    public static string GetDescription(DependencyObject element) => (string)element.GetValue(DescriptionProperty);

    private static void ScopeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement scope) return;
        if ((bool)e.NewValue)
        {
            scope.PreviewMouseMove += PointerMoved;
            scope.MouseLeave += PointerLeft;
            scope.GotKeyboardFocus += FocusChanged;
            scope.LostKeyboardFocus += FocusChanged;
            scope.AddHandler(ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler(ToolTipOpening), handledEventsToo: true);
            Update(scope, null);
        }
        else
        {
            scope.PreviewMouseMove -= PointerMoved;
            scope.MouseLeave -= PointerLeft;
            scope.GotKeyboardFocus -= FocusChanged;
            scope.LostKeyboardFocus -= FocusChanged;
            scope.RemoveHandler(ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler(ToolTipOpening));
            scope.ClearValue(TitlePropertyKey);
            scope.ClearValue(DescriptionPropertyKey);
        }
    }

    private static void DefaultTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement scope && GetIsScope(scope))
            Update(scope, scope.IsMouseOver ? Mouse.DirectlyOver as DependencyObject : null);
    }

    private static void PointerMoved(object sender, MouseEventArgs e)
    {
        for (var current = e.OriginalSource as DependencyObject; current is not null; current = Parent(current))
        {
            if (GetIsDisplay(current)) return;
            if (ReferenceEquals(current, sender)) break;
        }
        Update((FrameworkElement)sender, e.OriginalSource as DependencyObject);
    }

    private static void PointerLeft(object sender, MouseEventArgs e) => Update((FrameworkElement)sender, null);

    private static void FocusChanged(object sender, KeyboardFocusChangedEventArgs e) =>
        Update((FrameworkElement)sender, e.NewFocus as DependencyObject);

    private static void ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (FindText((FrameworkElement)sender, e.OriginalSource as DependencyObject) is null) return;
        e.Handled = true;
        Update((FrameworkElement)sender, e.OriginalSource as DependencyObject);
    }

    private static void Update(FrameworkElement scope, DependencyObject? source)
    {
        var text = FindText(scope, source) ?? FindText(scope, Keyboard.FocusedElement as DependencyObject)
            ?? GetDefaultText(scope);
        var lines = text.Split('\n', 2);
        scope.SetValue(TitlePropertyKey, lines[0].Trim());
        scope.SetValue(DescriptionPropertyKey, lines.Length > 1 ? lines[1].Trim() : string.Empty);
    }

    private static string? FindText(FrameworkElement scope, DependencyObject? source)
    {
        string? text = null;
        string? title = null;
        for (var current = source; current is not null; current = Parent(current))
        {
            if (text is null && ToolTipService.GetToolTip(current) is string tip && !string.IsNullOrWhiteSpace(tip))
                text = tip;
            if (title is null && System.Windows.Automation.AutomationProperties.GetName(current) is { Length: > 0 } name)
                title = name;
            if (ReferenceEquals(current, scope))
                return text is not null && !text.Contains('\n') && title is not null && title != text
                    ? $"{title}\n{text}" : text;
        }
        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) => element switch
    {
        Visual or Visual3D => VisualTreeHelper.GetParent(element),
        FrameworkContentElement content => content.Parent,
        _ => LogicalTreeHelper.GetParent(element),
    };
}
