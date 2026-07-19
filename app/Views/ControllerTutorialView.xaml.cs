using CodexController.Models;

namespace CodexController.Views;

public partial class ControllerTutorialView :
    System.Windows.Controls.UserControl
{
    private bool _voiceActive;

    public ControllerTutorialView()
    {
        InitializeComponent();
    }

    public void RenderControllerState(
        ControllerState state,
        double deadZone)
    {
        LeftStickTransform.X = state.LeftX * 8;
        LeftStickTransform.Y = -state.LeftY * 8;
        RightStickTransform.X = state.RightX * 8;
        RightStickTransform.Y = -state.RightY * 8;
        LeftStickHalo.Opacity =
            state.IsConnected &&
            Math.Max(Math.Abs(state.LeftX), Math.Abs(state.LeftY)) >
            deadZone
                ? 1
                : 0;
        RightStickHalo.Opacity =
            state.IsConnected &&
            Math.Max(Math.Abs(state.RightX), Math.Abs(state.RightY)) >
            deadZone
                ? 1
                : 0;
        LeftStickPressHalo.Opacity = state.Buttons.HasFlag(
            ControllerButtons.LeftThumb)
                ? 1
                : 0;
        RightStickPressHalo.Opacity = state.Buttons.HasFlag(
            ControllerButtons.RightThumb)
                ? 1
                : 0;
        DPadHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.DPadUp) ||
            state.Buttons.HasFlag(ControllerButtons.DPadDown) ||
            state.Buttons.HasFlag(ControllerButtons.DPadLeft) ||
            state.Buttons.HasFlag(ControllerButtons.DPadRight)
                ? 1
                : 0;
        ButtonAHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.A) ? 1 : 0;
        ButtonXHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.X) ? 1 : 0;
        ButtonBHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.B) ? 1 : 0;
        ButtonYHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.Y) ? 1 : 0;
        ViewButtonHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.Back) ? 1 : 0;
        MenuButtonHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.Start) ? 1 : 0;
        LeftShoulderHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.LeftShoulder) ? 1 : 0;
        RightShoulderHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.RightShoulder) ? 1 : 0;
        var physicalLeftTriggerOpacity =
            state.LeftTrigger > 0.03
                ? Math.Clamp(0.25 + state.LeftTrigger * 0.75, 0, 1)
                : 0;
        LeftTriggerHalo.Opacity = Math.Max(
            _voiceActive ? 1 : 0,
            physicalLeftTriggerOpacity);
        RightTriggerHalo.Opacity =
            state.RightTrigger > 0.03
                ? Math.Clamp(0.25 + state.RightTrigger * 0.75, 0, 1)
                : 0;
    }

    public void SetVoiceHalo(bool active)
    {
        _voiceActive = active;
        LeftTriggerHalo.Opacity = active ? 1 : 0;
    }
}
