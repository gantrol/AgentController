using AgentController.Platform.MacOS;

namespace AgentController.Platform.MacOS.Tests;

public sealed class MacPlatformSupportTests
{
    [Theory]
    [InlineData(14, MacPlatformAvailability.Supported)]
    [InlineData(15, MacPlatformAvailability.Supported)]
    [InlineData(26, MacPlatformAvailability.Supported)]
    [InlineData(13, MacPlatformAvailability.UnsupportedVersion)]
    public void UsesMacOsFourteenAsTheMinimumSupportedVersion(
        int majorVersion,
        MacPlatformAvailability expected)
    {
        Assert.Equal(
            expected,
            MacPlatformSupport.Evaluate(
                isMacOS: true,
                new Version(majorVersion, 0)));
    }

    [Fact]
    public void RejectsOtherOperatingSystemsWithoutInspectingTheirVersion()
    {
        Assert.Equal(
            MacPlatformAvailability.DifferentOperatingSystem,
            MacPlatformSupport.Evaluate(
                isMacOS: false,
                new Version(99, 0)));
    }
}
