using System.Windows;
using System.Windows.Input;
using CodexController.Controllers;
using CodexController.Models;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace CodexController.Views;

public sealed class ControllerJoystick : ControllerInputButton
{
    private static readonly DependencyPropertyKey OffsetXPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(OffsetX), typeof(double), typeof(ControllerJoystick), new PropertyMetadata(0d));
    private static readonly DependencyPropertyKey OffsetYPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(OffsetY), typeof(double), typeof(ControllerJoystick), new PropertyMetadata(0d));
    public static readonly DependencyProperty OffsetXProperty = OffsetXPropertyKey.DependencyProperty;
    public static readonly DependencyProperty OffsetYProperty = OffsetYPropertyKey.DependencyProperty;

    private bool _pointerDown;
    private bool _dragged;
    private int _direction;
    private Point _pointerOrigin;

    public double OffsetX => (double)GetValue(OffsetXProperty);
    public double OffsetY => (double)GetValue(OffsetYProperty);
    public LogicalInput StickInput => Input == LogicalInput.LeftStickPress
        ? LogicalInput.LeftStick : LogicalInput.RightStick;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!IsEnabled) return;
        Focus();
        if (!CaptureMouse()) return;
        _pointerDown = true;
        _dragged = false;
        var position = e.GetPosition(this);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        _pointerOrigin = (position - center).Length <= Math.Min(ActualWidth, ActualHeight) * 0.25
            ? position : center;
        UpdatePosition(position);
        if (!_dragged) BeginPress();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pointerDown) UpdatePosition(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (!_pointerDown) base.OnMouseLeave(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_pointerDown) return;
        UpdatePosition(e.GetPosition(this));
        var direction = _direction;
        var dragged = _dragged;
        _pointerDown = false;
        ResetPosition();
        RestoreHighlight();
        if (!dragged) FinishPress(false);
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (dragged)
        {
            if (direction != 0) SendDirection(direction);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var direction = e.Key switch { Key.Up => 1, Key.Right => 2, Key.Down => 3, Key.Left => 4, _ => 0 };
        if (direction == 0) { base.OnKeyDown(e); return; }
        e.Handled = true;
        if (!e.IsRepeat && IsEnabled && !_pointerDown)
        {
            FinishPress(true);
            SendDirection(direction);
        }
    }

    public override void CancelPress()
    {
        var wasDragging = _pointerDown;
        _pointerDown = false;
        ResetPosition();
        base.CancelPress();
        if (wasDragging) RestoreHighlight();
    }

    private void UpdatePosition(Point position)
    {
        var travel = Math.Max(1, Math.Min(ActualWidth, ActualHeight) * 0.24);
        var offset = position - _pointerOrigin;
        var length = offset.Length;
        var direction = length < travel * 0.3 ? 0
            : Math.Abs(offset.X) > Math.Abs(offset.Y) ? offset.X > 0 ? 2 : 4
            : offset.Y > 0 ? 3 : 1;
        if (length > travel) offset *= travel / length;
        if (direction != 0)
        {
            _dragged = true;
            FinishPress(true);
        }
        SetValue(OffsetXPropertyKey, offset.X);
        SetValue(OffsetYPropertyKey, offset.Y);
        if (_direction == direction) return;
        _direction = direction;
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this,
            direction == 0 ? Request : new TutorialInput(Mode, StickInput, direction), TutorialInputPhase.Highlighted));
    }

    private void ResetPosition()
    {
        _direction = 0;
        _dragged = false;
        SetValue(OffsetXPropertyKey, 0d);
        SetValue(OffsetYPropertyKey, 0d);
    }

    private void SendDirection(int direction)
    {
        var input = new TutorialInput(Mode, StickInput, direction);
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, input, TutorialInputPhase.Started));
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, input, TutorialInputPhase.Ended));
        RaiseEvent(new TutorialInputEventArgs(InputEvent, this, input, TutorialInputPhase.Tap));
    }

    private void RestoreHighlight() => RaiseEvent(new TutorialInputEventArgs(InputEvent, this, Request,
        IsMouseOver ? TutorialInputPhase.Highlighted : TutorialInputPhase.Unhighlighted));
}
