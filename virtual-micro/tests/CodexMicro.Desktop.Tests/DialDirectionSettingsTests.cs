using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class DialDirectionSettingsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultDirectionPreservesPhysicalEncoderDirection(
        bool physicalClockwise)
    {
        var settings = new DialDirectionSettings();

        Assert.Equal(
            physicalClockwise,
            settings.ToReportedClockwise(physicalClockwise));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvertedDirectionFlipsReportedEncoderDirection(
        bool physicalClockwise)
    {
        var settings = new DialDirectionSettings(invertDirection: true);

        Assert.Equal(
            !physicalClockwise,
            settings.ToReportedClockwise(physicalClockwise));
    }
}
