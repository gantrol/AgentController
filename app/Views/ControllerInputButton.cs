using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CodexController.Controllers;
using CodexController.Models;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace CodexController.Views;

public enum TutorialInputPhase { Started, Tap, Held, Ended, Canceled, Highlighted, Unhighlighted }

public sealed class TutorialInputEventArgs(
    RoutedEvent routedEvent, ControllerInputButton button,
    TutorialInput input, TutorialInputPhase phase) : RoutedEventArgs(routedEvent, button)
{
    public ControllerInputButton Button { get; } = button;
    public TutorialInput Input { get; } = input;
    public TutorialInputPhase Phase { get; } = phase;

    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget) =>
        ((EventHandler<TutorialInputEventArgs>)genericHandler)(genericTarget, this);
}

public class ControllerInputButton : Button
{
    public static readonly RoutedCommand InputCommand = new(nameof(InputCommand), typeof(ControllerInputButton));
    public static readonly RoutedEvent InputEvent = EventManager.RegisterRoutedEvent(
        "Input", RoutingStrategy.Bubble, typeof(EventHandler<TutorialInputEventArgs>), typeof(ControllerInputButton));
    public static readonly DependencyProperty InputProperty = DependencyProperty.Register(
        nameof(Input), typeof(LogicalInput), typeof(ControllerInputButton),
        new PropertyMetadata(LogicalInput.FaceSouth, OnInputChanged));
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(ControllerTutorialMode), typeof(ControllerInputButton),
        new PropertyMetadata(ControllerTutorialMode.Overview, OnInputChanged));
    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(int), typeof(ControllerInputButton), new PropertyMetadata(0, OnInputChanged));
    public static readonly DependencyProperty IsInputPressedProperty = DependencyProperty.Register(
        nameof(IsInputPressed), typeof(bool), typeof(ControllerInputButton), new PropertyMetadata(false));
    public static readonly DependencyProperty IsLinkedHighlightedProperty = DependencyProperty.Register(
        nameof(IsLinkedHighlighted), typeof(bool), typeof(ControllerInputButton), new PropertyMetadata(false));
    public static readonly DependencyProperty IsConfirmationPendingProperty = DependencyProperty.Register(
        nameof(IsConfirmationPending), typeof(bool), typeof(ControllerInputButton), new PropertyMetadata(false));
    public static readonly DependencyProperty HoldProgressProperty = DependencyProperty.Register(
        nameof(HoldProgress), typeof(double), typeof(ControllerInputButton), new PropertyMetadata(0d));

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private TutorialInput _pressedInput;
    private long _pressedAt;
    private bool _holdCompleted;
    private bool _keyboardPress;
    private Window? _window;

    public ControllerInputButton()
    {
        Command = InputCommand;
        CommandParameter = Request;
        _timer.Tick += (_, _) => UpdateHold();
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null) _window.Deactivated += WindowDeactivated;
        };
        Unloaded += (_, _) =>
        {
            CancelPress();
            if (_window is not null) _window.Deactivated -= WindowDeactivated;
            _window = null;
        };
        IsEnabledChanged += (_, _) => { if (!IsEnabled) CancelPress(); };
    }

    public LogicalInput Input { get => (LogicalInput)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public ControllerTutorialMode Mode { get => (ControllerTutorialMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public int Direction { get => (int)GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public bool IsInputPressed { get => (bool)GetValue(IsInputPressedProperty); private set => SetValue(IsInputPressedProperty, value); }
    public bool IsLinkedHighlighted { get => (bool)GetValue(IsLinkedHighlightedProperty); set => SetValue(IsLinkedHighlightedProperty, value); }
    public bool IsConfirmationPending { get => (bool)GetValue(IsConfirmationPendingProperty); set => SetValue(IsConfirmationPendingProperty, value); }
    public double HoldProgress { get => (double)GetValue(HoldProgressProperty); private set => SetValue(HoldProgressProperty, value); }
    public TutorialInput Request => new(Mode, Input, Direction);

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, Request, TutorialInputPhase.Highlighted));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (IsInputPressed && !_keyboardPress) CancelPress();
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, Request, TutorialInputPhase.Unhighlighted));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsEnabled) return;
        Focus();
        if (CaptureMouse()) BeginPress();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        FinishPress(!IsMouseOver);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelPress(); e.Handled = true; return; }
        if (e.Key is Key.Space or Key.Enter)
        {
            e.Handled = true;
            if (!e.IsRepeat && IsEnabled) { _keyboardPress = true; BeginPress(); }
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter && _keyboardPress)
        {
            e.Handled = true;
            FinishPress(false);
            return;
        }
        base.OnKeyUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (!_keyboardPress) CancelPress();
        base.OnLostMouseCapture(e);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        if (_keyboardPress) CancelPress();
        base.OnLostKeyboardFocus(e);
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, Request, TutorialInputPhase.Unhighlighted));
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, Request, TutorialInputPhase.Highlighted));
    }

    protected override void OnClick()
    {
        if (!IsEnabled) return;
        BeginPress();
        FinishPress(false);
    }

    public virtual void CancelPress()
    {
        FinishPress(true);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected void BeginPress()
    {
        if (IsInputPressed) return;
        _pressedInput = Request;
        _pressedAt = Environment.TickCount64;
        _holdCompleted = false;
        HoldProgress = 0;
        IsInputPressed = true;
        RaiseInput(TutorialInputPhase.Started);
        if (IsInputPressed && _pressedInput.HoldMilliseconds > 0) _timer.Start();
    }

    protected void FinishPress(bool canceled)
    {
        if (!IsInputPressed) return;
        _timer.Stop();
        IsInputPressed = false;
        _keyboardPress = false;
        HoldProgress = 0;
        RaiseInput(canceled ? TutorialInputPhase.Canceled : TutorialInputPhase.Ended);
        if (!canceled && !_holdCompleted && !_pressedInput.IsVoice)
            RaiseInput(TutorialInputPhase.Tap);
    }

    private void UpdateHold()
    {
        if (!IsInputPressed || !_keyboardPress && !IsMouseOver) { CancelPress(); return; }
        HoldProgress = Math.Clamp((Environment.TickCount64 - _pressedAt) / (double)_pressedInput.HoldMilliseconds, 0, 1);
        if (HoldProgress < 1) return;
        _timer.Stop();
        _holdCompleted = true;
        RaiseInput(TutorialInputPhase.Held);
    }

    private void RaiseInput(TutorialInputPhase phase) => RaiseEvent(new TutorialInputEventArgs(InputEvent, this, _pressedInput, phase));
    private void WindowDeactivated(object? sender, EventArgs e) => CancelPress();
    private static void OnInputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var button = (ControllerInputButton)sender;
        button.CancelPress();
        button.CommandParameter = button.Request;
    }
}
