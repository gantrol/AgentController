namespace CodexMicro.Desktop.Services;

internal enum VoicePressDecision
{
    Start,
    Stop,
    Ignore,
}

/// <summary>
/// Separates a physical button edge from the lifetime of voice capture.
/// Hold mode follows down/up; toggle mode latches on the first down edge and
/// ignores the matching up edge until a second down edge requests stop.
/// </summary>
internal static class VoiceGesturePolicy
{
    internal static VoicePressDecision Press(
        bool tapToToggle,
        bool voiceActive,
        bool voiceStopping)
    {
        if (voiceStopping)
        {
            return VoicePressDecision.Ignore;
        }

        if (!voiceActive)
        {
            return VoicePressDecision.Start;
        }

        return tapToToggle
            ? VoicePressDecision.Stop
            : VoicePressDecision.Ignore;
    }

    internal static bool StopOnRelease(bool tapToToggle) => !tapToToggle;

    internal static bool StopOnDeactivation(bool tapToToggle) => !tapToToggle;
}
