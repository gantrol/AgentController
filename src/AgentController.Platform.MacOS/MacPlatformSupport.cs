namespace AgentController.Platform.MacOS;

public enum MacPlatformAvailability
{
    Supported,
    UnsupportedVersion,
    DifferentOperatingSystem,
}

public static class MacPlatformSupport
{
    public const int MinimumMajorVersion = 14;

    public const string MinimumVersionDisplayName = "macOS 14 Sonoma";

    public static MacPlatformAvailability Current => Evaluate(
        OperatingSystem.IsMacOS(),
        Environment.OSVersion.Version);

    public static MacPlatformAvailability Evaluate(
        bool isMacOS,
        Version operatingSystemVersion)
    {
        ArgumentNullException.ThrowIfNull(operatingSystemVersion);
        if (!isMacOS)
        {
            return MacPlatformAvailability.DifferentOperatingSystem;
        }

        return operatingSystemVersion.Major >= MinimumMajorVersion
            ? MacPlatformAvailability.Supported
            : MacPlatformAvailability.UnsupportedVersion;
    }
}
