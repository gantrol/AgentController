using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexController.Controllers;
using CodexController.ViewModels;
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;

namespace CodexController.Views;

// The overview keeps both message keys mounted so changing columns preserves
// their input bindings. Only the three-column layout displays the combined pad.
public sealed class TutorialActionsPanel : Panel
{
    private const double ItemWidth = 130;
    private readonly List<UIElement> _active = [];
    private readonly Dictionary<UIElement, ItemLayout> _layouts = [];
    private int _columns;
    private int _arrangedColumns;

    private sealed class ItemLayout
    {
        public Rect Bounds { get; set; }
        public bool Active { get; set; }
        public int FadeVersion { get; set; }
        public TranslateTransform Offset { get; } = new();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _columns = double.IsPositiveInfinity(availableSize.Width)
            ? 3 : Math.Clamp((int)(availableSize.Width / ItemWidth), 1, 3);
        var hasPad = InternalChildren.Cast<UIElement>().Any(child => Item(child)?.IsDirectionalPad == true);
        _active.Clear();
        foreach (UIElement child in InternalChildren)
        {
            var item = Item(child);
            var show = item?.IsDirectionalPad == true ? _columns == 3
                : !hasPad || _columns != 3 || item?.Input is not (LogicalInput.DPadUp or LogicalInput.DPadDown);
            if (!show) continue;
            child.Visibility = Visibility.Visible;
            child.Measure(new Size(ItemWidth, double.PositiveInfinity));
            _active.Add(child);
        }

        var height = 0d;
        for (var index = 0; index < _active.Count; index += _columns) height += RowHeight(index);
        return new Size(Math.Min(availableSize.Width, Math.Min(_columns, _active.Count) * ItemWidth), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var motion = IsVisible && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
        var animate = motion && _arrangedColumns != 0 && _arrangedColumns != _columns;
        LogicalInput? focusedInput = null;
        var y = 0d;
        for (var index = 0; index < _active.Count; index += _columns)
        {
            var height = RowHeight(index);
            for (var column = 0; column < _columns && index + column < _active.Count; column++)
            {
                var child = _active[index + column];
                var bounds = new Rect(column * ItemWidth, y, ItemWidth, height);
                var layout = GetLayout(child);
                var wasActive = layout.Active;
                var fromX = wasActive ? layout.Bounds.X + layout.Offset.X - bounds.X : 0;
                var fromY = wasActive ? layout.Bounds.Y + layout.Offset.Y - bounds.Y : 0;
                child.Arrange(bounds);
                if (layout.Bounds != bounds || !wasActive || !motion)
                {
                    Move(layout.Offset, TranslateTransform.XProperty, fromX, animate);
                    Move(layout.Offset, TranslateTransform.YProperty, fromY, animate);
                }
                layout.Bounds = bounds;
                layout.Active = true;
                child.IsHitTestVisible = true;
                child.ClearValue(IsEnabledProperty);
                if (!wasActive || !motion) Fade(child, layout, animate);
            }
            y += height;
        }

        foreach (UIElement child in InternalChildren)
        {
            if (_active.Contains(child)) continue;
            var layout = GetLayout(child);
            if (layout.Active)
            {
                foreach (var button in ControllerTutorialView.InputButtons(child))
                {
                    if (button.IsKeyboardFocusWithin) focusedInput = button.Input;
                    button.CancelPress();
                }
            }
            var wasActive = layout.Active;
            layout.Active = false;
            child.IsHitTestVisible = false;
            child.IsEnabled = false;
            if (wasActive || !motion || child.Visibility != Visibility.Collapsed && layout.FadeVersion == 0)
                Fade(child, layout, animate && wasActive);
        }

        if (focusedInput is { } input)
        {
            var target = _active.SelectMany(ControllerTutorialView.InputButtons)
                .FirstOrDefault(button => button.Input == input && button.IsVisible && button.IsEnabled);
            target?.Focus();
        }
        foreach (var child in _layouts.Keys.Where(child => !InternalChildren.Contains(child)).ToArray())
            _layouts.Remove(child);
        _arrangedColumns = _columns;
        return finalSize;
    }

    private ItemLayout GetLayout(UIElement child)
    {
        if (_layouts.TryGetValue(child, out var layout)) return layout;
        layout = new ItemLayout();
        child.RenderTransform = layout.Offset;
        _layouts.Add(child, layout);
        return layout;
    }

    private double RowHeight(int start)
    {
        var height = 0d;
        for (var index = start; index < Math.Min(start + _columns, _active.Count); index++)
            height = Math.Max(height, _active[index].DesiredSize.Height);
        return height;
    }

    private void Move(TranslateTransform offset, DependencyProperty property, double from, bool animate)
    {
        offset.BeginAnimation(property, null);
        if (animate && Math.Abs(from) > 0.5)
            offset.BeginAnimation(property, new DoubleAnimation(from, 0, (Duration)FindResource("Duration.Base"))
            {
                EasingFunction = (IEasingFunction)FindResource("Ease.Out"),
                FillBehavior = FillBehavior.Stop,
            });
    }

    private void Fade(UIElement child, ItemLayout layout, bool animate)
    {
        var version = ++layout.FadeVersion;
        var from = child.Opacity;
        child.BeginAnimation(OpacityProperty, null);
        child.Opacity = layout.Active ? 1 : 0;
        if (!animate)
        {
            child.Visibility = layout.Active ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var fade = new DoubleAnimation(from, child.Opacity, (Duration)FindResource("Duration.Fast"))
        {
            FillBehavior = FillBehavior.Stop,
        };
        fade.Completed += (_, _) =>
        {
            if (layout.FadeVersion != version) return;
            child.BeginAnimation(OpacityProperty, null);
            if (!layout.Active) child.Visibility = Visibility.Collapsed;
        };
        child.BeginAnimation(OpacityProperty, fade);
    }

    private static ControllerTutorialItem? Item(UIElement child) =>
        (child as ContentPresenter)?.Content as ControllerTutorialItem;
}
