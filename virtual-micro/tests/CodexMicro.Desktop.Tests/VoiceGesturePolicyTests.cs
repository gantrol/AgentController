using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class VoiceGesturePolicyTests
{
    [Fact]
    public void HoldModeStartsOnPressAndStopsOnRelease()
    {
        Assert.Equal(
            VoicePressDecision.Start,
            VoiceGesturePolicy.Press(
                tapToToggle: false,
                voiceActive: false,
                voiceStopping: false));
        Assert.True(VoiceGesturePolicy.StopOnRelease(tapToToggle: false));
        Assert.True(VoiceGesturePolicy.StopOnDeactivation(tapToToggle: false));
    }

    [Fact]
    public void ToggleModeIgnoresReleaseAndStopsOnSecondPress()
    {
        Assert.Equal(
            VoicePressDecision.Start,
            VoiceGesturePolicy.Press(
                tapToToggle: true,
                voiceActive: false,
                voiceStopping: false));
        Assert.False(VoiceGesturePolicy.StopOnRelease(tapToToggle: true));
        Assert.False(VoiceGesturePolicy.StopOnDeactivation(tapToToggle: true));
        Assert.Equal(
            VoicePressDecision.Stop,
            VoiceGesturePolicy.Press(
                tapToToggle: true,
                voiceActive: true,
                voiceStopping: false));
    }

    [Fact]
    public void PressesAreIgnoredWhileVoiceIsStopping()
    {
        Assert.Equal(
            VoicePressDecision.Ignore,
            VoiceGesturePolicy.Press(
                tapToToggle: true,
                voiceActive: false,
                voiceStopping: true));
    }
}
