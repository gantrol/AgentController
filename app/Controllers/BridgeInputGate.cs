using CodexController.Models;

namespace CodexController.Controllers;

/// <summary>
/// Keeps the bridge switch as a single controller-input boundary.
/// Presentation can still mirror the physical device while every control
/// intent is swallowed before it reaches Codex.
/// </summary>
public static class BridgeInputGate
{
    public static bool HasControlIntent(
        ControllerState state,
        double stickDeadZone,
        double triggerThreshold = 0.12)
    {
        var safeDeadZone = Math.Clamp(stickDeadZone, 0.18, 0.95);
        var safeTriggerThreshold = Math.Clamp(
            triggerThreshold,
            0.05,
            0.95);
        return
            state.Buttons != ControllerButtons.None ||
            Math.Abs(state.LeftX) >= safeDeadZone ||
            Math.Abs(state.LeftY) >= safeDeadZone ||
            Math.Abs(state.RightX) >= safeDeadZone ||
            Math.Abs(state.RightY) >= safeDeadZone ||
            state.LeftTrigger >= safeTriggerThreshold ||
            state.RightTrigger >= safeTriggerThreshold;
    }
}
