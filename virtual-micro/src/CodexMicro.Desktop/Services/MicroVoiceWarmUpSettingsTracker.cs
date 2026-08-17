namespace CodexMicro.Desktop.Services;

internal sealed class MicroVoiceWarmUpSettingsTracker
{
    private MicroVoiceProfile? _settings;

    internal bool Update(MicroVoiceProfile settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_settings == settings)
        {
            return false;
        }

        _settings = settings;
        return true;
    }
}
