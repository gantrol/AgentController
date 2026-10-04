using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ToolTip = System.Windows.Controls.ToolTip;

namespace CodexController.Views;

internal static class HelpTip
{
    private static readonly DependencyProperty ActiveTipProperty = DependencyProperty.RegisterAttached(
        "ActiveTip", typeof(ToolTip), typeof(HelpTip));

    public static void Toggle(FrameworkElement owner)
    {
        if (owner.GetValue(ActiveTipProperty) is ToolTip active)
        {
            active.IsOpen = false;
            return;
        }

        var hint = new ToolTip { PlacementTarget = owner, Placement = PlacementMode.Bottom, VerticalOffset = 10 };
        hint.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("ToolTip") { Source = owner });
        var hoverEnabled = ToolTipService.GetIsEnabled(owner);
        ToolTipService.SetIsEnabled(owner, false);
        owner.SetValue(ActiveTipProperty, hint);
        owner.MouseLeave += CloseOnLeave;
        owner.LostKeyboardFocus += CloseOnFocusLost;
        owner.PreviewKeyDown += CloseOnEscape;
        owner.Unloaded += Close;
        hint.Closed += Cleanup;
        hint.IsOpen = true;

        void CloseOnLeave(object sender, System.Windows.Input.MouseEventArgs e) => hint.IsOpen = false;
        void CloseOnFocusLost(object sender, KeyboardFocusChangedEventArgs e) => hint.IsOpen = false;
        void Close(object sender, RoutedEventArgs e) => hint.IsOpen = false;
        void CloseOnEscape(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            hint.IsOpen = false;
            e.Handled = true;
        }
        void Cleanup(object sender, RoutedEventArgs e)
        {
            owner.MouseLeave -= CloseOnLeave;
            owner.LostKeyboardFocus -= CloseOnFocusLost;
            owner.PreviewKeyDown -= CloseOnEscape;
            owner.Unloaded -= Close;
            hint.Closed -= Cleanup;
            owner.ClearValue(ActiveTipProperty);
            ToolTipService.SetIsEnabled(owner, hoverEnabled);
        }
    }
}
